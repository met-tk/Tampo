using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 全局提示音服务：支持学习标记音效、中键复制音效
    /// 默认采用 Windows 原生清脆系统音效，并支持导入与播放用户自定义音频（.wav/.mp3等）
    /// </summary>
    public class SoundService
    {
        private static SoundService? _instance;
        public static SoundService Instance => _instance ??= new SoundService();

        private const string KeySoundEnabled = "Sound_IsEnabled";
        private const string KeyStudyMarkPath = "Sound_StudyMarkPath";
        private const string KeyCopyPath = "Sound_CopyPath";

        private const string DefaultStudyWav = @"C:\Windows\Media\Windows Navigation Start.wav";
        private const string DefaultCopyWav = @"C:\Windows\Media\Windows Ding.wav";

        private const uint SND_ASYNC = 0x0001;
        private const uint SND_FILENAME = 0x00020000;
        private const uint SND_NODEFAULT = 0x0002;

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MessageBeep(uint uType);

        private MediaPlayer? _mediaPlayer;

        private SoundService()
        {
        }

        public bool IsEnabled
        {
            get => UserPreferenceService.Instance.GetBool(KeySoundEnabled, true);
            set => UserPreferenceService.Instance.SetBool(KeySoundEnabled, value);
        }

        public string StudyMarkSoundPath
        {
            get => UserPreferenceService.Instance.Get(KeyStudyMarkPath, string.Empty);
            set => UserPreferenceService.Instance.Set(KeyStudyMarkPath, value);
        }

        public string CopySoundPath
        {
            get => UserPreferenceService.Instance.Get(KeyCopyPath, string.Empty);
            set => UserPreferenceService.Instance.Set(KeyCopyPath, value);
        }

        /// <summary>
        /// 播放【学习】标记单词音效
        /// </summary>
        public void PlayStudyMarkSound()
        {
            if (!IsEnabled) return;
            PlaySoundFileOrFallback(StudyMarkSoundPath, DefaultStudyWav, 0x00000040 /* MB_ICONASTERISK */);
        }

        /// <summary>
        /// 播放鼠标中键复制成功音效
        /// </summary>
        public void PlayCopySound()
        {
            if (!IsEnabled) return;
            PlaySoundFileOrFallback(CopySoundPath, DefaultCopyWav, 0 /* MB_OK */);
        }

        /// <summary>
        /// 试听指定音频文件或系统默认音效
        /// </summary>
        public void TestPlay(string? customPath, bool isStudySound)
        {
            string fallbackFile = isStudySound ? DefaultStudyWav : DefaultCopyWav;
            uint fallbackBeep = isStudySound ? 0x00000040u : 0u;
            PlaySoundFileOrFallback(customPath, fallbackFile, fallbackBeep);
        }

        private void PlaySoundFileOrFallback(string? customPath, string fallbackSystemWav, uint fallbackBeepType)
        {
            Task.Run(() =>
            {
                try
                {
                    // 1. 如果配置了自定义音频文件且存在
                    if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                    {
                        PlayAudioFile(customPath);
                        return;
                    }

                    // 2. 默认使用 Windows 系统媒体目录中的高品质 wav
                    if (File.Exists(fallbackSystemWav))
                    {
                        PlaySound(fallbackSystemWav, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
                        return;
                    }

                    // 3. 兜底使用 Windows 系统蜂鸣提示音
                    MessageBeep(fallbackBeepType);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "SoundService.PlaySound");
                }
            });
        }

        private void PlayAudioFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".wav")
            {
                PlaySound(filePath, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }
            else
            {
                // mp3, m4a, aac 等媒体格式通过 WinRT MediaPlayer 播放
                App.RunOnUIThread(() =>
                {
                    try
                    {
                        _mediaPlayer ??= new MediaPlayer();
                        _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(filePath));
                        _mediaPlayer.Play();
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, "SoundService.MediaPlayer");
                    }
                });
            }
        }
    }
}
