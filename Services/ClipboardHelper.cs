using System;
using Windows.ApplicationModel.DataTransfer;

namespace NihongoVocab.Services
{
    public static class ClipboardHelper
    {
        /// <summary>
        /// 将指定文本复制到系统剪贴板
        /// </summary>
        /// <param name="text">要复制的文本内容</param>
        /// <returns>是否复制成功</returns>
        public static bool CopyText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var dataPackage = new DataPackage();
                dataPackage.RequestedOperation = DataPackageOperation.Copy;
                dataPackage.SetText(text.Trim());
                Clipboard.SetContent(dataPackage);
                SoundService.Instance.PlayCopySound();
                return true;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ClipboardHelper.CopyText");
                return false;
            }
        }
    }
}
