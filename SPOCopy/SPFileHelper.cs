using Microsoft.SharePoint.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SPOCopy
{
    class SPFileHelper
    {
        public static void UploadDocument(ClientContext clientContext, string sourceFilePath, string serverRelativeDestinationPath, string fileName)
        {
            Microsoft.SharePoint.Client.File file;

            if (TryGetFileByServerRelativeUrl(clientContext.Web, serverRelativeDestinationPath + "/" + fileName, out file))
            {
                file.CheckOut();
            }

            var folder = clientContext.Web.GetFolderByServerRelativeUrl(serverRelativeDestinationPath);
            clientContext.Load(folder);
            clientContext.ExecuteQueryRetry();

            byte[] array = System.IO.File.ReadAllBytes(sourceFilePath);
            using (MemoryStream stream = new MemoryStream(array))
            {
                Microsoft.SharePoint.Client.File uploadFile = folder.UploadFile(fileName, stream, true);

                uploadFile.CheckIn("", CheckinType.MajorCheckIn);
                //uploadFile.Approve("");
                clientContext.Load(uploadFile);
                clientContext.ExecuteQueryRetry();
            }
        }

        public static void UploadFoldersRecursively(ClientContext clientContext, System.IO.DirectoryInfo folderInfo, Folder folder)
        {
            System.IO.FileInfo[] files = null;
            System.IO.DirectoryInfo[] subDirs = null;

            try
            {
                files = folderInfo.GetFiles("*.*");
            }
            catch (UnauthorizedAccessException e)
            {
                Console.WriteLine(e.Message);
            }

            catch (System.IO.DirectoryNotFoundException e)
            {
                Console.WriteLine(e.Message);
            }

            if (files != null)
            {
                foreach (System.IO.FileInfo fi in files)
                {
                    Console.WriteLine(fi.FullName);
                    clientContext.Load(folder);
                    clientContext.ExecuteQueryRetry();

                    string destPath = fi.FullName.Substring(fi.FullName.IndexOf("Style Library") + "Style Library".Length).Replace("\\", "/");

                    UploadDocument(clientContext, fi.FullName, folder.ServerRelativeUrl + destPath.Replace("/" + fi.Name, ""), fi.Name);
                }

                subDirs = folderInfo.GetDirectories();

                foreach (System.IO.DirectoryInfo dirInfo in subDirs)
                {
                    Folder subFolder = folder.Folders.Add(dirInfo.Name);
                    clientContext.ExecuteQueryRetry();
                    UploadFoldersRecursively(clientContext, dirInfo, subFolder);
                }
            }
        }

        public static void UploadFolderStructure(ClientContext clientContext, Folder rootFolder, string sourcePath, string folderStartsFrom)
        {
            string relPath = "";

            if (sourcePath.Contains(folderStartsFrom))
            {
                relPath = sourcePath.Substring(sourcePath.IndexOf(folderStartsFrom) + folderStartsFrom.Length);

                string[] splitPaths;

                if (relPath.Contains("\\"))
                {
                    splitPaths = relPath.Split('\\');
                }
                else
                {
                    splitPaths = new string[] { relPath };
                }

                Folder subFolder = rootFolder;

                foreach (string s in splitPaths)
                {
                    if (IsFolder(s))
                    {
                        FolderCollection subFolders = subFolder.Folders;
                        clientContext.Load(subFolders);
                        clientContext.ExecuteQueryRetry();

                        var folderExists = subFolders.Any(x => x.Name == s);

                        if (!folderExists)
                        {
                            subFolder = CreateFolder(clientContext, subFolder, s);
                        }
                        else
                        {
                            subFolder = subFolders.FirstOrDefault(x => x.Name == s);
                        }
                    }
                }
            }
        }

        public static bool IsFolder(string fileName)
        {
            bool isFolder = !string.IsNullOrEmpty(fileName) && !fileName.Contains(".");

            return isFolder;
        }

        private static Folder CreateFolder(ClientContext clientContext, Folder parent, string folderName)
        {
            Folder folder = parent.Folders.Add(folderName);
            clientContext.ExecuteQueryRetry();

            return folder;
        }

        private static bool TryGetFileByServerRelativeUrl(Web web, string serverRelativeUrl, out Microsoft.SharePoint.Client.File file)
        {
            var ctx = web.Context;
            try
            {
                file = web.GetFileByServerRelativeUrl(serverRelativeUrl);
                ctx.Load(file);
                ctx.ExecuteQuery();
                return true;
            }
            catch (Microsoft.SharePoint.Client.ServerException ex)
            {
                if (ex.ServerErrorTypeName == "System.IO.FileNotFoundException")
                {
                    file = null;
                    return false;
                }
                else
                    throw;
            }
        }
    }
}
