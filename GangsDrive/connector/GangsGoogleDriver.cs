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

        private UserCredential _userCredential;
        private DriveService _driveService;

        public GangsGoogleDriver(string mountPoint)
            :base(mountPoint, "Google")
        {
        }

        private string ToUnixStylePath(string winPath)
        {
            return string.Format(@"/{0}", winPath.Replace(@"\", @"/").Replace("//", "/"));
        }

        #region Implementation of IDokanOperations
        public void Cleanup(string fileName, DokanFileInfo info)
        {
            if (info.Context != null && info.Context is FileStream)
            {
                (info.Context as FileStream).Dispose();
            }
            info.Context = null;
        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {
            if (info.Context != null && info.Context is FileStream)
            {
                (info.Context as FileStream).Dispose();
            }
            info.Context = null;
        }

        public NtStatus CreateDirectory(string fileName, DokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
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
            string path = ToUnixStylePath(fileName);
            IList<Google.Apis.Drive.v2.Data.File> file_list = GetChildrenById(GetIdByPath(path));

            files = new List<FileInformation>();

            foreach (var file in file_list)
            {
                FileInformation finfo = new FileInformation()
                {
                    FileName = file.Title,
                    CreationTime = (file.CreatedDate.HasValue) ? file.CreatedDate.Value : new DateTime(1970, 1, 1),
                    Length = (file.FileSize.HasValue) ? file.FileSize.Value : 0,
                    Attributes = (IsDirectory(file)) ? FileAttributes.Directory : FileAttributes.Normal
                };

                files.Add(finfo);
            }

            return DokanResult.Success;
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            if (info.Context != null)
            {
                (info.Context as FileStream).Flush();

                return DokanResult.Success;
            }
            else return DokanResult.Unsuccessful;
            
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
            string path = ToUnixStylePath(fileName);
            Google.Apis.Drive.v2.Data.File file_obj = GetFileById(GetIdByPath(path));

            fileInfo = new FileInformation()
            {
                FileName = file_obj.Title,
                CreationTime = (file_obj.CreatedDate.HasValue) ? file_obj.CreatedDate.Value : new DateTime(1970, 1, 1),
                Length = (file_obj.FileSize.HasValue) ? file_obj.FileSize.Value : 0,
                Attributes = (IsDirectory(file_obj)) ? FileAttributes.Directory : FileAttributes.Normal
            };

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
            try
            {
                string path = ToUnixStylePath(fileName);
                if (info.Context == null)
                {
                    Google.Apis.Drive.v2.Data.File file_obj = GetFileById(GetIdByPath(path));

                    if (IsDirectory(file_obj))
                    {
                        bytesRead = 0;
                        return DokanResult.AccessDenied;
                    }

                    var web_stream = _driveService.HttpClient.GetStreamAsync(file_obj.DownloadUrl);
                    var result = web_stream.Result;
 
                    using (var stream = result)
                    {
                        //result.CopyTo(stream);
                        stream.Position = offset;
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                }
                else
                {
                    FileStream stream = info.Context as FileStream;

                    lock (stream)
                    {
                        stream.Position = offset;
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                }

                return DokanResult.Success;
            }
            catch (FileNotFoundException e)
            {
                GangsDrive.util.DriverError.DebugError(e, _driverName, _isMounted);
                bytesRead = 0;
                return DokanResult.FileNotFound;
            }
        }

        public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            if (info.Context != null)
            {
                (info.Context as FileStream).SetLength(length);
                return DokanResult.Success;
            }
            else return DokanResult.Unsuccessful;
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            if (info.Context != null)
            {
                (info.Context as FileStream).SetLength(length);
                return DokanResult.Success;
            }
            else return DokanResult.Unsuccessful;
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
            bytesWritten = 0;
            return DokanResult.Success;
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

            using (var stream = new FileStream("C:\\Users\\snownymph\\Documents\\GitHub\\GangsDrive\\GangsDrive\\credentials\\google_secret.json", System.IO.FileMode.Open, System.IO.FileAccess.Read))
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
            path = path.Substring(1);
            if (path == "/" || path == "")
                return "root";

            string[] path_list = path.Split('/');
            List<Google.Apis.Drive.v2.Data.File> file_search_list = new List<Google.Apis.Drive.v2.Data.File>();

            FilesResource.ListRequest req = _driveService.Files.List();
            do
            {
                req.Q = "title='" + path_list.Last<string>() + "'";
                FileList file_search = req.Execute();
                file_search_list.AddRange(file_search.Items);
            } while (!String.IsNullOrEmpty(req.PageToken));

            if (file_search_list.Count == 1)
            {
                return file_search_list.First<Google.Apis.Drive.v2.Data.File>().Id;
            }
            else
            {
                int last_idx = path_list.Length - 2;
                Google.Apis.Drive.v2.Data.File ret = new Google.Apis.Drive.v2.Data.File();
                foreach (Google.Apis.Drive.v2.Data.File f in file_search_list)
                {
                    if (GetRecursiveParent(path, f.Parents, last_idx) != null)
                    {
                        ret = f;
                        break;
                    }
                }

                return ret.Id;
            }
        }
        public List<Google.Apis.Drive.v2.Data.File> GetChildrenById(string id)
        {
            List<Google.Apis.Drive.v2.Data.File> result = new List<Google.Apis.Drive.v2.Data.File>();

            ChildrenResource.ListRequest child_req = _driveService.Children.List(id);
            ChildList ch = child_req.Execute();

            foreach (ChildReference a in ch.Items)
            {
                FilesResource.GetRequest get_file = _driveService.Files.Get(a.Id);
                Google.Apis.Drive.v2.Data.File file_obj = get_file.Execute();
                result.Add(file_obj);
            }

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
            if (id != null)
            {
                FilesResource.GetRequest file = _driveService.Files.Get(id);
                return file.Execute();
            }
            else return new Google.Apis.Drive.v2.Data.File();
        }
        #endregion
    }
}
