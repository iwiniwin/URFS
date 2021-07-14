using System.Collections;
using System.Collections.Generic;
using RemoteFileExplorer.Editor.UI;
using UnityEngine;
using UnityEditor;
using System.IO;
using System;

namespace RemoteFileExplorer.Editor
{
    public class Manipulator
    {
        public string m_CurPath;

        private const string CacheKey = "RemoteFileExplorer_Cache_GoTo";
        private List<string> m_GoToHistory = new List<string>();
        private int m_GoToHistoryIndex = -1;
        private Coroutine m_GoToCoroutine;

        public string curPath
        {
            get
            {
                return m_CurPath;
            }
            set
            {
                m_CurPath = FileUtil.FixedPath(value);
            }
        }

        private RemoteFileExplorerWindow m_Owner;
        public Manipulator(RemoteFileExplorerWindow owner)
        {
            m_Owner = owner;
        }

        public void UpdateStatusInfo()
        {
            Coroutines.Start(Internal_UpdateStatusInfo());
        }

        public void Refresh()
        {
            if(string.IsNullOrEmpty(curPath)) return;
            GoTo(curPath, false, false, false);
        }

        public void GoTo(ObjectItem item)
        {
            var data = item.Data;
            if (data.type == ObjectType.File)
                return;
            GoTo(data.path);
        }

        public void GoTo(string path)
        {
            GoTo(path, false, true, false);
        }

        public void GoToByKey(string key)
        {
            GoTo(key, true, true, false);
        }

        public void GoTo(string path, bool isKey, bool record, bool silent)
        {
            if(m_GoToCoroutine != null)
            {
                Coroutines.Stop(m_GoToCoroutine);
            }
            m_GoToCoroutine = Coroutines.Start(Internal_GoTo(path, isKey, record, silent));
        }

        public void RecordGoTo(string path)
        {
            if(m_GoToHistoryIndex >= 0 && m_GoToHistory.Count > m_GoToHistoryIndex)
            {
                if(m_GoToHistory[m_GoToHistoryIndex].Equals(path)) 
                {
                    return;
                }
            }
            if(m_GoToHistoryIndex >= 0 && m_GoToHistoryIndex < m_GoToHistory.Count - 1)
            {
                m_GoToHistory.RemoveRange(m_GoToHistoryIndex + 1, m_GoToHistory.Count - m_GoToHistoryIndex - 1);
            }
            m_GoToHistory.Add(path);
            m_GoToHistoryIndex = m_GoToHistory.Count - 1;
            if(m_GoToHistoryIndex > 0)
            {
                m_Owner.m_PrevButton.SetEnabled(true);
            }
            m_Owner.m_NextButton.SetEnabled(false);
        }

        public void BackwardGoTo()
        {
            if(m_GoToHistoryIndex > 0)
            {
                GoTo(m_GoToHistory[-- m_GoToHistoryIndex], false, false, false);
                m_Owner.m_NextButton.SetEnabled(true);
            }
            if(m_GoToHistoryIndex <= 0)
            {
                m_Owner.m_PrevButton.SetEnabled(false);
            }
        }

        public void ForwardGoTo()
        {
            if(m_GoToHistoryIndex < m_GoToHistory.Count - 1)
            {
                GoTo(m_GoToHistory[++ m_GoToHistoryIndex], false, false, false);
                m_Owner.m_PrevButton.SetEnabled(true);
            }
            if(m_GoToHistoryIndex >= m_GoToHistory.Count - 1)
            {
                m_Owner.m_NextButton.SetEnabled(false);
            }
        }

        /// <summary>
        /// 记录上次的GOTO，下次启动后自动跳转
        /// </summary>
        public void SaveLastGoTo()
        {
            if(m_GoToHistoryIndex >= 0 && m_GoToHistory.Count > m_GoToHistoryIndex)
            {
                EditorUserSettings.SetConfigValue(CacheKey, m_GoToHistory[m_GoToHistoryIndex]);
            }
        }

        public string ReadLastGoTo()
        {
            if(m_GoToHistoryIndex >= 0 && m_GoToHistory.Count > m_GoToHistoryIndex)  // 窗口未关闭时再次开启连接，直接使用内存记录
            {
                return m_GoToHistory[m_GoToHistoryIndex];
            }
            return EditorUserSettings.GetConfigValue(CacheKey);
        }

        public void Select(ObjectItem item)
        {
            var data = item.Data;
            curPath = data.path;
            m_Owner.m_ObjectListArea.SetSelectItem(item);
        }

        /// <summary>
        /// 选择空
        /// </summary>
        public void Select()
        {
            ObjectItem item = m_Owner.m_ObjectListArea.GetSelectItem();
            if (item != null)
            {
                curPath = Path.GetDirectoryName(item.Data.path);
            }
            m_Owner.m_ObjectListArea.SetSelectItem(null);
        }

        public void Download(ObjectItem item)
        {
            var data = item.Data;
            string path = data.path;
            string dest = null;
            if(data.type == ObjectType.File)
            {
                string extension = Path.GetExtension(path);
                if(extension.StartsWith("."))
                {
                    extension = extension.Substring(1, extension.Length - 1);
                }
                dest = EditorUtility.SaveFilePanel(Constants.SelectFileTitle, "", Path.GetFileNameWithoutExtension(path), extension);
                if(string.IsNullOrEmpty(dest)) return;
            }
            else
            {
                dest = EditorUtility.SaveFolderPanel(Constants.SelectFileTitle, "", "");
                if(string.IsNullOrEmpty(dest)) return;
                dest = FileUtil.CombinePath(dest, Path.GetFileName(path));
            }
            Coroutines.Start(Internal_Download(path, dest));
        }

        public void Delete(ObjectItem item)
        {
            Coroutines.Start(Internal_Delete(item.Data.path));
        }

        public void Rename(ObjectItem item)
        {
            // Coroutines.Start(Internal_Rename(item.Data.path));
        }

        public void UploadFile()
        {
            string path = EditorUtility.OpenFilePanel(Constants.SelectFileTitle, "", "");
            if (!string.IsNullOrEmpty(path))
            {
                Upload(new string[] { path });
            }
        }

        public void UploadFolder()
        {
            string path = EditorUtility.OpenFolderPanel(Constants.SelectFolderTitle, "", "");
            if (!string.IsNullOrEmpty(path))
            {
                Upload(new string[] { path });
            }
        }

        public void Upload(string[] paths)
        {
            string dest = curPath;
            ObjectItem item = m_Owner.m_ObjectListArea.GetSelectItem();
            if (item != null)
            {
                dest = Path.GetDirectoryName(item.Data.path);
            }
            if (string.IsNullOrEmpty(dest))
            {
                EditorUtility.DisplayDialog(Constants.WindowTitle, Constants.NoDestPathTip, Constants.OkText);
                return;
            }
            foreach (string path in paths)
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, string.Format(Constants.PathNotExistTip, path), Constants.OkText);
                    return;
                }
            }
            Coroutines.Start(Internal_Upload(paths, dest));
        }

        /// <summary>
        /// 跳转到指定路径
        /// </summary>
        private IEnumerator Internal_GoTo(string path, bool isKey, bool record, bool silent)
        {
            if (!CheckConnectStatus(!silent)) yield break;
            Command req;
            if (isKey)
            {
                req = new QueryPathKeyInfo.Req
                {
                    PathKey = path,
                };
            }
            else
            {
                req = new QueryPathInfo.Req
                {
                    Path = path,
                };
            }
            CommandHandle handle = m_Owner.m_Server.Send(req);
            yield return handle;
            if (!CheckHandleError(handle, "", !silent) || !CheckCommandError(handle.Command, "", !silent))
            {
                yield break;
            }
            var rsp = handle.Command as QueryPathInfo.Rsp;
            if (!rsp.Exists)
            {
                string msg = string.Format(Constants.PathNotExistTip, path);
                if(rsp is QueryPathKeyInfo.Rsp)
                {
                    msg = string.Format(Constants.PathKeyNotExistTip, path, (rsp as QueryPathKeyInfo.Rsp).Path);
                }
                Log.Debug(msg);
                if(!silent)
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, msg, Constants.OkText);
                }
                yield break;
            }
            List<ObjectData> list = new List<ObjectData>();

            foreach (var item1 in rsp.Directories)
            {
                list.Add(new ObjectData(ObjectType.Folder, item1));
            }
            foreach (var item1 in rsp.Files)
            {
                list.Add(new ObjectData(ObjectType.File, item1));
            }
            m_Owner.m_ObjectListArea.UpdateView(list);
            if (rsp is QueryPathKeyInfo.Rsp)
            {
                curPath = (rsp as QueryPathKeyInfo.Rsp).Path;
            }
            else
            {
                curPath = path;
            }
            if(record)
            {
                RecordGoTo(curPath);
            }
        }

        /// <summary>
        /// 下载
        /// </summary>
        private IEnumerator Internal_Download(string path, string dest)
        {
            if (!CheckConnectStatus()) yield break;
            var req = new Pull.Req
            {
                Path = path,
            };
            CommandHandle handle = m_Owner.m_Server.Send(req);
            yield return handle;
            string downloadFailedTip = string.Format(Constants.DownloadFailedTip, path);
            while (CheckHandleError(handle, downloadFailedTip) && CheckCommandError(handle.Command, downloadFailedTip))
            {
                if (handle.Command is CreateDirectory.Req)
                {
                    
                    var createDirectoryReq = handle.Command as CreateDirectory.Req;
                    CreateDirectory.Rsp rsp = new CreateDirectory.Rsp()
                    {
                        Ack = createDirectoryReq.Seq,
                    };
                    try
                    {
                        foreach (string directory in ConvertPaths(path, dest, createDirectoryReq.Directories))
                        {
                            if(!Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        rsp.Error = e.Message;
                    }
                    m_Owner.m_Server.Send(rsp);
                    if (!CheckCommandError(rsp, downloadFailedTip))
                    {
                        yield break;
                    }
                    handle.Finished = false;
                    yield return handle;
                }
                else if (handle.Command is TransferFile.Req)
                {
                    var transferFileReq = handle.Command as TransferFile.Req;
                    TransferFile.Rsp rsp = new TransferFile.Rsp()
                    {
                        Ack = transferFileReq.Seq,
                    };
                    try
                    {
                        File.WriteAllBytes(ConvertPath(path, dest, transferFileReq.Path), transferFileReq.Content);
                    }
                    catch (Exception e)
                    {
                        rsp.Error = e.Message;
                    }
                    m_Owner.m_Server.Send(rsp);
                    if (!CheckCommandError(rsp, downloadFailedTip))
                    {
                        yield break;
                    }
                    handle.Finished = false;
                    yield return handle;
                }
                else if (handle.Command is Pull.Rsp)
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, string.Format(Constants.DownloadSuccessTip, path), Constants.OkText);
                    yield break;
                }
                else
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, downloadFailedTip + Constants.UnknownError, Constants.OkText);
                    yield break;
                }
            }
        }

        private IEnumerator Internal_Upload(string[] paths, string dest)
        {
            if (!CheckConnectStatus()) yield break;
            string uploadConfirmTip = string.Format(Constants.UploadConfirmTip, "\n", string.Join("\n", paths), dest);
            bool ret = EditorUtility.DisplayDialog(Constants.WindowTitle, uploadConfirmTip, Constants.OkText, Constants.CancelText);
            if (!ret)
            {
                yield break;
            }
            string curGoToPath = curPath;
            foreach (string path in paths)
            {
                string error = null;
                string[] directories = null;
                string[] files = null;
                string curDest = FileUtil.CombinePath(dest, Path.GetFileName(path));  // dest一定是路径
                if (File.Exists(path))
                {
                    files = new string[] { path };
                }
                else
                {
                    directories = FileUtil.GetAllDirectories(path);
                    files = FileUtil.GetAllFiles(path);
                }
                if (directories != null)
                {
                    CreateDirectory.Req req = new CreateDirectory.Req()
                    {
                        Directories = ConvertPaths(path, curDest, directories),
                    };
                    CommandHandle handle = m_Owner.m_Server.Send(req);
                    yield return handle;
                    if (handle.Error != null)
                    {
                        error = handle.Error;
                    }
                    else if (!string.IsNullOrEmpty(handle.Command.Error))
                    {
                        error = handle.Command.Error;
                    }
                }
                if (error == null)
                {
                    foreach (string file in files)
                    {
                        byte[] content;
                        try
                        {
                            content = File.ReadAllBytes(file);
                        }
                        catch (Exception e)
                        {
                            error = e.Message;
                            break;
                        }
                        TransferFile.Req req = new TransferFile.Req()
                        {
                            Path = ConvertPath(path, curDest, file),
                            Content = content,
                        };
                        CommandHandle handle = m_Owner.m_Server.Send(req);
                        yield return handle;
                        if (handle.Error != null)
                        {
                            error = handle.Error;
                            break;
                        }
                        else if (!string.IsNullOrEmpty(handle.Command.Error))
                        {
                            error = handle.Command.Error;
                            break;
                        }
                    }
                }
                if (error == null)
                {
                    if(curGoToPath == curPath)
                    {
                        Refresh();
                    }
                    EditorUtility.DisplayDialog(Constants.WindowTitle, string.Format(Constants.UploadSuccessTip, path), Constants.OkText);
                }
                else
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, string.Format(Constants.UploadFailedTip, path) + error, Constants.OkText);
                }
            }
        }

        private IEnumerator Internal_Delete(string path)
        {
            if (!CheckConnectStatus()) yield break;
            string curGoToPath = curPath;
            string deleteConfirmTip = string.Format(Constants.DeleteConfirmTip, path);
            bool ret = EditorUtility.DisplayDialog(Constants.WindowTitle, deleteConfirmTip, Constants.OkText, Constants.CancelText);
            if (!ret)
            {
                yield break;
            }
            var req = new Delete.Req(){
                Path = path,
            };
            CommandHandle handle = m_Owner.m_Server.Send(req);
            yield return handle;
            string deleteFailedTip = string.Format(Constants.DeleteFailedTip, path);
            if (!CheckHandleError(handle, deleteFailedTip) || !CheckCommandError(handle.Command, deleteFailedTip))
            {
                yield break;
            }
            if(curGoToPath == curPath)
            {
                GoTo(Directory.GetParent(curPath).ToString(), false, false, false);  // 刷新
            }
            EditorUtility.DisplayDialog(Constants.WindowTitle, string.Format(Constants.DeleteSuccessTip, path), Constants.OkText);
        }

        private IEnumerator Internal_Rename(string path, string newPath)
        {
            if (!CheckConnectStatus()) yield break;
            var req = new Rename.Req(){
                Path = path,
                NewPath = newPath,
            };
            CommandHandle handle = m_Owner.m_Server.Send(req);
            yield return handle;
            string renameFailedTip = string.Format(Constants.RenameFailedTip, path);
            if (!CheckHandleError(handle, renameFailedTip) || !CheckCommandError(handle.Command, renameFailedTip))
            {
                yield break;
            }
            EditorUtility.DisplayDialog(Constants.WindowTitle, string.Format(Constants.RenameSuccessTip, path), Constants.OkText);
        }

        private IEnumerator Internal_UpdateStatusInfo()
        {
            if (!CheckConnectStatus(false))
            {
                yield return null;  // 下一帧执行，保证在主线程更新UI
                m_Owner.m_DeviceNameLabel.text = Constants.UnknownText;
                m_Owner.m_DeviceModelLabel.text = Constants.UnknownText;
                m_Owner.m_DeviceSystemLabel.text = Constants.UnknownText;
                m_Owner.titleContent.image = TextureUtility.GetTexture("project");
                m_Owner.m_ConnectStateLabel.text = "Unconnected";
                m_Owner.m_ConnectStateLabel.style.color = Color.red;
                yield break;
            }
            CommandHandle handle = m_Owner.m_Server.Send(new QueryDeviceInfo.Req());
            yield return handle;
            if(handle.Error == null && string.IsNullOrEmpty(handle.Command.Error))
            {
                var rsp = handle.Command as QueryDeviceInfo.Rsp;
                m_Owner.m_DeviceNameLabel.text = rsp.Name;
                m_Owner.m_DeviceModelLabel.text = rsp.Model;
                m_Owner.m_DeviceSystemLabel.text = rsp.System;
                m_Owner.titleContent.image = TextureUtility.GetTexture("project active");
                m_Owner.m_ConnectStateLabel.text = "Established";
                m_Owner.m_ConnectStateLabel.style.color = Color.green;

                // 自动跳转到上次路径
                string path = ReadLastGoTo();
                if(!string.IsNullOrEmpty(path))
                {
                    GoTo(path, false, true, true);
                }
            }
        }

        public string[] ConvertPaths(string src, string dest, string[] curs)
        {
            string[] paths = new string[curs.Length];
            for (int i = 0; i < curs.Length; i++)
            {
                paths[i] = ConvertPath(src, dest, curs[i]);
            }
            return paths;
        }

        public string ConvertPath(string src, string dest, string cur)
        {
            src = FileUtil.FixedPath(src);
            if(src.EndsWith("/")) src = src.Substring(0, src.Length - 1);
            dest = FileUtil.FixedPath(dest);
            cur = FileUtil.FixedPath(cur);
            return dest + cur.Replace(src, "");
        }

        public bool CheckConnectStatus(bool displayDialog = true)
        {
            if (m_Owner.m_Server.Status == ConnectStatus.Connected)
            {
                return true;
            }
            if(displayDialog)
            {
                EditorUtility.DisplayDialog(Constants.WindowTitle, Constants.NotConnectedTip, Constants.OkText);
            }
            return false;
        }

        public bool CheckHandleError(CommandHandle handle, string tip, bool displayDialog = true)
        {
            if (handle.Error != null)
            {
                if(displayDialog)
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, tip + "handle.Error", Constants.OkText);
                }
                return false;
            }
            return true;
        }

        public bool CheckCommandError(Command command, string tip, bool displayDialog = true)
        {
            if (!string.IsNullOrEmpty(command.Error))
            {
                Log.Error(tip + command.Error);
                if(displayDialog)
                {
                    EditorUtility.DisplayDialog(Constants.WindowTitle, tip + command.Error, Constants.OkText);
                }
                return false;
            }
            return true;
        }
    }
}