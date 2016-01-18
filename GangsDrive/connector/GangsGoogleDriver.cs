using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v2;
using Google.Apis.Drive.v2.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using DokanNet;
using FileAccess = DokanNet.FileAccess;
using System.Runtime.Caching;

namespace GangsDrive.connector
{
    public class GangsGoogleDriver : GangsDriver, IDokanOperations
    {
        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                 FileAccess.Execute |
                                 FileAccess.GenericExecute | FileAccess.GenericWrite | FileAccess.GenericRead;

        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
                                                   FileAccess.Delete |
                                                   FileAccess.GenericWrite;

        private string[] Scopes = { 
                                      DriveService.Scope.Drive, 
                                      DriveService.Scope.DriveFile, 
                                      DriveService.Scope.DriveMetadata 
                                  };
        private const string ApplicationName = "GangsDrive";

        private MemoryCache _cache = MemoryCache.Default;

        private UserCredential _userCredential;
        private DriveService _driveService;

        public GangsGoogleDriver(string mountPoint)
            : base(mountPoint, "Google")
        {
        }

        private string ToUnixStylePath(string winPath)
        {
            return string.Format(@"/{0}", winPath.Replace(@"\", @"/").Replace("//", "/"));
        }

        #region Implementation of IDokanOperations
        public void Cleanup(string fileName, DokanFileInfo info)
        {

        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {

        }

        public NtStatus CreateDirectory(string fileName, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            Debug.Print("[CreateFile] fileName : {0}, mode : {1}", fileName, mode);

            if (fileName.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("autorun.inf", StringComparison.OrdinalIgnoreCase))
            {
                return DokanResult.FileNotFound;
            }

            string path = ToUnixStylePath(fileName);
            string id = GetIdByPath(path);
            bool exists = (id != null) ? true : false;

            if (info.IsDirectory && mode == System.IO.FileMode.CreateNew)
                return DokanResult.AccessDenied;

            if (info.IsDirectory)
            {
                switch (mode)
                {
                    case FileMode.Open:
                        if (!exists)
                            return DokanResult.PathNotFound;

                        break;

                    case FileMode.CreateNew:
                        if (exists)
                            return DokanResult.FileExists;

                        //create directory
                        CreateDirectory(path);
                        break;
                }
            }
            else
            {
                switch (mode)
                {
                    case FileMode.Open:
                        if (!exists)
                            return DokanResult.FileNotFound;

                        info.Context = GetFileById(id);
                        info.IsDirectory = IsDirectory(info.Context as Google.Apis.Drive.v2.Data.File);

                        break;

                    default:
                        break;
                }
            }

            //else
            //{
            //    bool readWriteAttributes = (access & DataAccess) == 0;
            //    bool readAccess = (access & DataWriteAccess) == 0;
            //    System.IO.FileAccess acs = readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite;
            //    Google.Apis.Drive.v2.Data.File file_obj = GetFileById(id);

            //    switch (mode)
            //    {
            //        case FileMode.Open:

            //            if (!exists)
            //                return DokanResult.FileNotFound;

            //            bool isDir = IsDirectory(file_obj);

            //            if (readWriteAttributes || isDir)
            //            {
            //                info.IsDirectory = isDir;
            //                info.Context = new object();
            //                return DokanResult.Success;
            //            }

            //            break;

            //        case FileMode.CreateNew:
            //            if (exists)
            //                return DokanResult.FileExists;

            //            // upload empty file

            //            // cache invalidate

            //            break;

            //        case FileMode.Truncate:
            //            if (!exists)
            //                return DokanResult.FileNotFound;

            //            // cache invalidate

            //            break;

            //        default:
            //            // cache invalidate
            //            break;
            //    }

            //    try
            //    {
            //        //var web_stream = _driveService.HttpClient.GetStreamAsync(file_obj.DownloadUrl);
            //        //web_stream.Wait();
            //        //info.Context = web_stream.Result;
            //        info.Context = file_obj;
            //    }
            //    catch (Exception e)
            //    {
            //        return DokanResult.AccessDenied;
            //    }
            //}

            return DokanResult.Success;
        }

        public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus DeleteFile(string fileName, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus EnumerateNamedStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, DokanFileInfo info)
        {
            streamName = String.Empty;
            streamSize = 0;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            Debug.Print("[FindFiles] filename : {0}", fileName);

            string path = ToUnixStylePath(fileName);
            IList<Google.Apis.Drive.v2.Data.File> file_list = GetChildrenById(GetIdByPath(path));

            files = new List<FileInformation>();

            foreach (var file in file_list)
            {
                FileInformation finfo = new FileInformation()
                {
                    FileName = file.Title,
                    CreationTime = (file.CreatedDate.HasValue) ? file.CreatedDate.Value : new DateTime(1970, 1, 1),
                    LastAccessTime = new DateTime(1970, 1, 1),
                    LastWriteTime = new DateTime(1970, 1, 1),
                    Length = (file.FileSize.HasValue) ? file.FileSize.Value : 0,
                    Attributes = FileAttributes.NotContentIndexed,
                };

                if (IsDirectory(file))
                {
                    finfo.Attributes |= FileAttributes.Directory;
                    finfo.Length = 0;
                }

                files.Add(finfo);
            }

            return DokanResult.Success;
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus GetDiskFreeSpace(out long free, out long total, out long used, DokanFileInfo info)
        {
            free = 512 * 1024 * 1024;
            total = 1024 * 1024 * 1024;
            used = 512 * 1024 * 1024;

            return DokanResult.Success;
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
        {
            Debug.Print("[GetFileInformation] fileName : {0}, info.Context : {1}", fileName, info.Context == null ? "null" : "notnull");

            string path = ToUnixStylePath(fileName);
            Google.Apis.Drive.v2.Data.File file_obj = GetFileById(GetIdByPath(path));

            fileInfo = new FileInformation()
            {
                FileName = file_obj.Title,
                CreationTime = (file_obj.CreatedDate.HasValue) ? file_obj.CreatedDate.Value : new DateTime(1970, 1, 1),
                LastAccessTime = new DateTime(1970, 1, 1),
                LastWriteTime = new DateTime(1970, 1, 1),
                Length = (file_obj.FileSize.HasValue) ? file_obj.FileSize.Value : 0,
                Attributes = FileAttributes.NotContentIndexed,
            };

            if (IsDirectory(file_obj))
            {
                fileInfo.Attributes |= FileAttributes.Directory;
                fileInfo.Length = 0;
            }

            return DokanResult.Success;
        }

        public NtStatus GetFileSecurity(string fileName, out System.Security.AccessControl.FileSystemSecurity security, System.Security.AccessControl.AccessControlSections sections, DokanFileInfo info)
        {
            security = null;
            return DokanResult.Error;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, DokanFileInfo info)
        {
            volumeLabel = _userCredential.UserId;
            fileSystemName = "GangsDrive";

            features = FileSystemFeatures.None;

            return DokanResult.Success;
        }

        public NtStatus LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus OpenDirectory(string fileName, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            Debug.Print("[ReadFile] fileName : {0}", fileName);

            //if(buffer.Length < 10)
            //{
            //    bytesRead = 0;
            //    return DokanResult.Error;
            //}

            //for (int i = 0; i < 10; i++)
            //{
            //    buffer[i] = 0x47;
            //}
            //bytesRead = 10;
            //return DokanResult.Success;


            Google.Apis.Drive.v2.Data.File file_obj;

            Debug.Print("read : {0} offset : {1}", fileName, offset);

            if (info.Context == null)
            {
                if (info.IsDirectory)
                {
                    bytesRead = 0;
                    return DokanResult.AccessDenied;
                }

                file_obj = GetFileById(GetIdByPath(ToUnixStylePath(fileName)));
            }
            else
            {
                file_obj = info.Context as Google.Apis.Drive.v2.Data.File;
            }



            _driveService.HttpClient.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + buffer.Length - 1);

            try
            {
                Task<Stream> task = _driveService.HttpClient.GetStreamAsync(file_obj.DownloadUrl);
                task.Wait();
                var stream = task.Result;

                Debug.Print("받았다!!");

                bytesRead = stream.Read(buffer, 0, buffer.Length);
            }
            catch (AggregateException e)
            {
                bytesRead = 0;
                Debug.Print("aggregate exception : {0}----------------------------------", e.Message);
                foreach (var ex in e.InnerExceptions)
                {
                    Debug.Print(ex.StackTrace);
                }
                Debug.Print("-----------------------------------------------------------");
                return DokanResult.Error;
            }

            // restore Range header
            _driveService.HttpClient.DefaultRequestHeaders.Range = new System.Net.Http.Headers.RangeHeaderValue();

            return DokanResult.Success;
        }

        public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus SetFileSecurity(string fileName, System.Security.AccessControl.FileSystemSecurity security, System.Security.AccessControl.AccessControlSections sections, DokanFileInfo info)
        {
            return DokanResult.Error;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus Unmount(DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            Debug.Print("[WriteFile] fileName : {0}", fileName);

            bytesWritten = 0;
            return DokanResult.Error;
        }

        public NtStatus FindStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, DokanFileInfo info)
        {
            streamName = String.Empty;
            streamSize = 0;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            streams = new FileInformation[0];
            return DokanResult.NotImplemented;
        }
        #endregion

        #region Overriding of GangsDriver

        public override void Mount()
        {
            if (IsMounted)
                return;

            using (var stream = new FileStream(@"C:\Users\pknam\Documents\GangsDrive\GangsDrive\credentials\google_secret.json", System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                var credentials = GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.Load(stream).Secrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(CredentialPath, true));
                _userCredential = credentials.Result;
                if (credentials.IsCanceled || credentials.IsFaulted)
                    throw new Exception("cannot connect");

                _driveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = _userCredential,
                    ApplicationName = ApplicationName,
                });
            }

            base.Mount();
        }

        public override void ClearMountPoint()
        {
            if (!IsMounted)
                return;

            base.ClearMountPoint();

            _driveService.Dispose();
        }
        #endregion

        #region GoogleDrive API Helper
        public Google.Apis.Drive.v2.Data.File CreateDirectory(string path)
        {
            path = path.Substring(1);
            string file_name = path.Substring(path.LastIndexOf('/'), path.Length);
            string parent_path = path.Substring(0, path.LastIndexOf('/'));

            Google.Apis.Drive.v2.Data.File body = new Google.Apis.Drive.v2.Data.File();
            body.Title = file_name;
            body.MimeType = "application/vnd.google-apps.folder";
            body.Parents = new List<ParentReference>() 
            {
                new ParentReference() { Id = GetIdByPath(parent_path) } 
            };

            return _driveService.Files.Insert(body).Execute();
        }
        public string GetRecursiveParent(string path, IList<ParentReference> parent, int idx)
        {
            string[] path_list = path.Split('/');
            FilesResource.GetRequest parent_req = _driveService.Files.Get(parent[0].Id);
            Google.Apis.Drive.v2.Data.File parent_file = parent_req.Execute();

            if (parent[0].IsRoot.Value)
                return parent[0].Id;
            if (parent_file.Title == path_list[idx] && !parent[0].IsRoot.Value)
                return GetRecursiveParent(path, parent_file.Parents, idx - 1);
            else
                return null;
        }

        public string GetIdByPath(string path)
        {
            Debug.Print("[GetIdBypath] path : {0}", path);

            path = path.Replace("//", "");
            if (path == "/" || path == "")
                return "root";


            object cachedId = _cache.Get("pathid:" + path);
            if (cachedId != null)
            {
                Debug.Print("[Cache Hit] path to id : {0}", path);
                return (string)cachedId;
            }




            string[] path_list = path.Split('/');




            string currentDirectoryId = "root";

            for (int i = 0; i < path_list.Length; i++)
            {
                FilesResource.ListRequest req = _driveService.Files.List();
                req.Q = string.Format("title='{0}' and '{1}' in parents", path_list[i], currentDirectoryId);
                FileList files = req.Execute();

                if (files.Items.Count == 0)
                    return null;

                currentDirectoryId = files.Items[0].Id;

                string curPath = string.Join("/", path_list.Take(i + 1));
                _cache.Set("pathid:" + curPath, currentDirectoryId, DateTimeOffset.Now.AddSeconds(10));
            }



            return currentDirectoryId;




            //List<Google.Apis.Drive.v2.Data.File> file_search_list = new List<Google.Apis.Drive.v2.Data.File>();

            //try
            //{
            //    FilesResource.ListRequest req = _driveService.Files.List();
            //    do
            //    {
            //        req.Q = "title='" + path_list.Last<string>() + "'";
            //        FileList file_search = req.Execute();
            //        file_search_list.AddRange(file_search.Items);
            //    } while (!String.IsNullOrEmpty(req.PageToken));
            //}
            //catch (IOException io)
            //{
            //    return null;
            //}

            //if (file_search_list.Count == 1)
            //{
            //    return file_search_list.First<Google.Apis.Drive.v2.Data.File>().Id;
            //}
            //else if (file_search_list.Count > 1)
            //{
            //    int last_idx = path_list.Length - 2;
            //    Google.Apis.Drive.v2.Data.File ret = new Google.Apis.Drive.v2.Data.File();
            //    foreach (Google.Apis.Drive.v2.Data.File f in file_search_list)
            //    {
            //        if (GetRecursiveParent(path, f.Parents, last_idx) != null)
            //        {
            //            ret = f;
            //            break;
            //        }
            //    }

            //    return ret.Id;
            //}
            //else return null;
        }

        public List<Google.Apis.Drive.v2.Data.File> GetChildrenById(string id)
        {
            List<Google.Apis.Drive.v2.Data.File> result = new List<Google.Apis.Drive.v2.Data.File>();

            FilesResource.ListRequest req = _driveService.Files.List();
            req.Q = "trashed=false";
            req.Q += string.Format(" and '{0}' in parents", id);

            FileList files = req.Execute();
            result = files.Items.ToList<Google.Apis.Drive.v2.Data.File>();

            foreach(var file in result)
                _cache.Set("idfile:" + file.Id, file, DateTimeOffset.Now.AddSeconds(10));

            return result;
        }

        public bool IsDirectory(Google.Apis.Drive.v2.Data.File file)
        {
            if (!file.Copyable.HasValue)
            {
                return false;
            }

            return (!file.Copyable.Value && file.MimeType == "application/vnd.google-apps.folder");
        }

        public Google.Apis.Drive.v2.Data.File GetFileById(string id)
        {
            object cachedFile = _cache.Get("idfile:" + id);
            if (cachedFile != null)
                return (Google.Apis.Drive.v2.Data.File)cachedFile;


            if (id != null)
            {
                FilesResource.GetRequest fileRequest = _driveService.Files.Get(id);
                Google.Apis.Drive.v2.Data.File file = fileRequest.Execute();

                _cache.Set("idfile:" + id, file, DateTimeOffset.Now.AddSeconds(10));

                return file;
            }
            else return new Google.Apis.Drive.v2.Data.File();
        }
        #endregion
    }
}
