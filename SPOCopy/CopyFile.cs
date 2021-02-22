using EnvDTE;
using EnvDTE80;
using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OfficeDevPnP.Core;
using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace SPOCopy
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class CopyFile
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("c1281013-817a-4e2b-ba95-cd6060ca5893");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="CopyFile"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private CopyFile(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static CopyFile Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in CopyFile's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new CopyFile(package, commandService);


        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void Execute(object sender, EventArgs e)
        {
            SPOCopyPackage spoCopyPackage = package as SPOCopyPackage;

            if(string.IsNullOrEmpty(spoCopyPackage.SiteCollectionUrl) || string.IsNullOrEmpty(spoCopyPackage.Username) || string.IsNullOrEmpty(spoCopyPackage.Password))
            {
                VsShellUtilities.ShowMessageBox(
                               this.package,
                               "Missingi configuration. Provide Site Url and Credentials frm Tools -> Options -> SPO COpy",
                               "SPO Copy - Invalid configuration",
                               OLEMSGICON.OLEMSGICON_CRITICAL,
                               OLEMSGBUTTON.OLEMSGBUTTON_OK,
                               OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            IVsOutputWindow outWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            Guid customGuid = new Guid("0F44E2D1-F5FA-4d2d-AB30-22BE8ECD9789");
            IVsOutputWindowPane customPane;

            string customTitle = "SPO Copy";
            outWindow.CreatePane(ref customGuid, customTitle, 1, 1);
            outWindow.GetPane(ref customGuid, out customPane);
            customPane.Activate();

            customPane.OutputString(Environment.NewLine + "SPO Copy started");

            try
            {
                //ThreadHelper.ThrowIfNotOnUIThread();
                string message = string.Format(CultureInfo.CurrentCulture, "Inside {0}.MenuItemCallback()", this.GetType().FullName);
                string title = "CopyFile";

                AuthenticationManager am = new AuthenticationManager();

                customPane.OutputString(Environment.NewLine + "Connecting to SharePoint Online");

                using (ClientContext cc = am.GetSharePointOnlineAuthenticatedContextTenant(spoCopyPackage.SiteCollectionUrl, spoCopyPackage.Username, spoCopyPackage.Password))
                {
                    Web web = cc.Web;
                    cc.Load(web);
                    cc.Load(web.Lists);
                    cc.ExecuteQueryRetry();

                    List list = web.Lists.GetByTitle("Style Library");
                    cc.Load(list);
                    cc.ExecuteQueryRetry();

                    Folder rootFolder = list.RootFolder;
                    cc.Load(rootFolder);
                    cc.ExecuteQueryRetry();

                    customPane.OutputString(Environment.NewLine + $"Connected to {spoCopyPackage.SiteCollectionUrl}");

                    var dte = await package.GetServiceAsync(typeof(DTE)).ConfigureAwait(false) as DTE2;

                    EnvDTE.SelectedItems selectedItems = dte.SelectedItems;

                    if (selectedItems != null)
                    {
                        foreach (EnvDTE.SelectedItem selectedItem in selectedItems)
                        {
                            EnvDTE.ProjectItem projectItem = selectedItem.ProjectItem as EnvDTE.ProjectItem;

                            if (projectItem != null)
                            {
                                string projectItemName = projectItem.Name;
                                string path = projectItem.Properties.Item("FullPath").Value.ToString();

                                message = $"Called on {projectItemName}";
                                string destPath = string.Empty;

                                if (!string.IsNullOrEmpty(path) && path.Contains("Style Library"))
                                {
                                    if (SPFileHelper.IsFolder(projectItemName))
                                    {
                                        customPane.OutputString(Environment.NewLine + $"Uploading folder {projectItemName} - {path}");

                                        System.IO.DirectoryInfo di = new System.IO.DirectoryInfo(path);

                                        SPFileHelper.UploadFolderStructure(cc, rootFolder, path, "Style Library");
                                        SPFileHelper.UploadFoldersRecursively(cc, di, rootFolder);

                                        customPane.OutputString(Environment.NewLine + $"Upload completed.");
                                    }
                                    else
                                    {
                                        customPane.OutputString($"Uploading file {projectItemName} - {path}");

                                        SPFileHelper.UploadFolderStructure(cc, rootFolder, path, "Style Library");

                                        destPath = path.Substring(path.IndexOf("Style Library") + "Style Library".Length).Replace("\\", "/");

                                        SPFileHelper.UploadDocument(cc, path, list.RootFolder.ServerRelativeUrl + destPath.Replace("/" + projectItemName, ""), projectItemName);

                                        customPane.OutputString(Environment.NewLine + $"Upload completed.");
                                    }
                                }
                                else
                                {
                                    customPane.OutputString((Environment.NewLine +  $"Selected file or folder is not a Style Library item");
                                    return;
                                }

                            }
                        }

                        // Show a message box to prove we were here
                        VsShellUtilities.ShowMessageBox(
                            this.package,
                            "File copied successfully",
                            title,
                            OLEMSGICON.OLEMSGICON_INFO,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                }

                //UIHierarchy uih = (UIHierarchy)dte.Windows.Item(
                //    EnvDTE.Constants.vsWindowKindSolutionExplorer).Object;
                //Array selectedItems = (Array)uih.SelectedItems;
                //foreach (UIHierarchyItem selectedItem in selectedItems)
                //{
                //    // Show a message box to prove we were here
                //    VsShellUtilities.ShowMessageBox(
                //        this.package,
                //        selectedItem.Name,
                //        "Selected Project",
                //        OLEMSGICON.OLEMSGICON_INFO,
                //        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                //        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                //}
            }
            catch (Exception ex)
            {
                customPane.OutputString(Environment.NewLine + $"Error: {ex.Message}");

                VsShellUtilities.ShowMessageBox(
                               this.package,
                               ex.Message,
                               "SPO Copy",
                               OLEMSGICON.OLEMSGICON_CRITICAL,
                               OLEMSGBUTTON.OLEMSGBUTTON_OK,
                               OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

    }
}

