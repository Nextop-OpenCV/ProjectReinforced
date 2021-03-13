﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading;
using System.Windows;

using OpenCvSharp;
using OpenCvSharp.Extensions;

using DesktopDuplication;

using ProjectReinforced.Clients;
using ProjectReinforced.Types;
using ProjectReinforced.Others;
using ProjectReinforced.Properties;

using Size = OpenCvSharp.Size;

namespace ProjectReinforced.Recording
{
    public class Screen
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rectangle
        {
            public int left;
            public int top;
            public int right;
            public int bottom;

            public static implicit operator System.Drawing.Rectangle(Rectangle rect)
            {
                return new System.Drawing.Rectangle(rect.left, rect.top, rect.right, rect.bottom);
            }
        }

        private static VideoWriter _videoWriter;
        private static Queue<ScreenCaptured> _screenshots = new Queue<ScreenCaptured>();
        private static DesktopDuplicator _dd = new DesktopDuplicator(0);

        private static bool _isWorking;

        public static bool IsRecording { get; private set; }
        public static bool IsDisposed { get; private set; }

        public static async Task Record(IGameClient game)
        {
            if (game == null) return;

            Rectangle rect = new Rectangle
            {
                left = 0,
                right = 1920,
                top = 0,
                bottom = 1080
            };

            if (Win32.GetWindowRect(game.GameProcess.MainWindowHandle, ref rect))
            {
                await Task.Run(() => Start(game, rect));
            }
            else
            {
                ExceptionManager.ShowError("현재 플레이 중인 게임 윈도우를 찾지 못했습니다.", "게임 윈도우를 찾지 못함", nameof(Screen),
                    nameof(Record));
            }
        }

        private static void Start(IGameClient game, System.Drawing.Rectangle bounds)
        {
            int resolution = Settings.Default.Screen_Resolution;
            int seconds = Settings.Default.Screen_RecordTimeBeforeHighlight;
            int fps = Settings.Default.Screen_Fps;

            bool isUnfixed = fps == 0;
            //큐의 최대 크기 (고정되지 않은 프레임 방식이면 무한)
            int maxSize = !isUnfixed ? fps * seconds : -1;
            int frameDelay = !isUnfixed ? 1000 / fps : 0;

            ScreenCaptured lastScreen = null;
            IsRecording = true;

            //스크린샷 시 실행 되는 코드
            while (IsRecording)
            {
                if (game.IsRunning && game.IsActive) //게임을 키고 현재 플레이 중인 경우
                {
                    if (!isUnfixed && _screenshots.Count > maxSize)
                    {
                        ScreenCaptured sc = _screenshots.Dequeue();
                        sc.Frame.Dispose();
                    }

                    //프레임 가져오기
                    Mat frame = Screenshot(bounds)?.ToMat();

                    if (frame != null)
                    {
                        //해상도 조절
                        if (resolution != 0)
                        {
                            Size size = new Size(1920, 1080);

                            if (resolution == 720) size = new Size(1280, 720); //720p (HD)
                            frame.Resize(size);
                        }

                        lastScreen = new ScreenCaptured(frame);
                        _screenshots.Enqueue(lastScreen);
                    }
                    else
                    {
                        if (lastScreen != null)
                        {
                            lastScreen.CountToUse++;
                        }
                    }
                }
                else if (!game.IsRunning)
                {
                    IsRecording = false;
                    ClearScreenshots();
                }
                else if (!game.IsActive)
                {
                    ClearScreenshots();
                    Thread.Sleep(30);
                }
            }
        }

        public static Highlight Stop(HighlightInfo info)
        {
            if (_screenshots.Count <= 0 || !IsRecording) return null;

            int secondsAfterHighlight = Settings.Default.Screen_RecordTimeAfterHighlight;
            bool isUnfixed = Settings.Default.Screen_Fps == 0;

            if (secondsAfterHighlight > 0) Thread.Sleep(secondsAfterHighlight * 1000);

            IsRecording = false;
            _isWorking = true;

            Highlight highlight = new Highlight(info);
            FourCC fcc = FourCC.FromString(Settings.Default.Screen_FourCC);

            if (File.Exists(highlight.VideoPath)) File.Delete(highlight.VideoPath);

            Mat baseScreen = _screenshots.Peek().Frame;
            Size size = baseScreen.Size();
            double fps = Settings.Default.Screen_Fps;

            string path = Highlight.GetVideoPath(info);
            string directoryPath = Path.GetDirectoryName(path);

            //고정되지 않은 프레임 방식 (저장된 프레임의 수에 따라서 fps가 결정됨)
            if (isUnfixed) fps = (double)_screenshots.Count / (Settings.Default.Screen_RecordTimeBeforeHighlight + secondsAfterHighlight);

            if (File.Exists(path)) File.Delete(path);
            if (directoryPath != null && !Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            _videoWriter = new VideoWriter(path, fcc, fps, size);

            if (!_videoWriter.IsOpened())
            {
                _videoWriter.Release();
                baseScreen.Dispose();

                ExceptionManager.ShowError("하이라이트 영상을 저장할 수 없습니다.", "저장할 수 없음", nameof(Screen), nameof(Stop));
                return null;
            }

            ScreenCaptured lastScreen = null;

            while (_screenshots.Count > 0)
            {
                if (lastScreen == null || lastScreen.CountToUse == 0)
                {
                    lastScreen?.Frame?.Dispose(); //lastScreen 변수가 null이면 이 함수는 실행되지 않음
                    lastScreen = _screenshots.Dequeue();
                }

                if (lastScreen.Frame.Size() != size)
                {
                    lastScreen.Frame.Resize(size);
                }

                _videoWriter.Write(lastScreen.Frame);
                lastScreen.CountToUse--;
            }

            _videoWriter.Release();
            baseScreen.Dispose();
            _isWorking = false;

            return highlight;
        }

        public static async Task<Highlight> StopAsync(HighlightInfo info)
        {
            return await Task.Run(() => Stop(info));
        }
        #region Debugging Functions
        #region Record Function
        public static void RecordForDebug(double fps, int seconds)
        {
            Rectangle rect = new Rectangle //1920x1080 해상도
            {
                left = 0,
                top = 0,
                right = 1920,
                bottom = 1080
            };
            Size size = new Size(1920, 1080);

            Task.Run(() => StartForDebug(rect, size, fps, seconds));
        }
        #endregion
        #region Start Function
        private static void StartForDebug(Rectangle bounds, Size size, double fps, int seconds)
        {
            bool isUnfixed = (int)fps == 0;
            //큐의 최대 크기 (고정되지 않은 프레임 방식이면 무한)
            int maxSize = !isUnfixed ? (int)fps * seconds : -1;
            int frameDelay = !isUnfixed ? 1000 / (int)fps : 0;

            ScreenCaptured lastScreen = null;

            Stopwatch sw = new Stopwatch();
            IsRecording = true;

            while (IsRecording)
            {
                sw.Start();

                if (!isUnfixed && _screenshots.Count > maxSize)
                {
                    ScreenCaptured sc = _screenshots.Dequeue();
                    sc.Frame.Dispose();
                }

                //프레임 가져오기
                Mat frame = Screenshot(bounds)?.ToMat();

                if (frame != null)
                {
                    //해상도 조절
                    if (frame.Size() != size)
                    {
                        frame.Resize(size);
                    }

                    lastScreen = new ScreenCaptured(frame);
                    _screenshots.Enqueue(lastScreen);

                    sw.Stop();
                    Trace.WriteLine($"{sw.ElapsedMilliseconds}ms elapsed.");

                    //int delay = Math.Max(0, frameDelay - (int)sw.ElapsedMilliseconds);
                    sw.Reset();
                }
                else
                {
                    if (lastScreen != null)
                    {
                        lastScreen.CountToUse++;
                    }
                }
            }
        }
        #endregion
        #region Stop Function
        public static bool StopForDebug(GameType game, double fps, int secondsBeforeHighlight, int secondsAfterHighlight, out int frameCount)
        {
            if (_screenshots.Count <= 0 || !IsRecording)
            {
                frameCount = 0;
                return false;
            }
;
            bool isUnfixed = (int)fps == 0;
            if (secondsAfterHighlight > 0) Thread.Sleep(secondsAfterHighlight * 1000);

            IsRecording = false;
            _isWorking = true;
            frameCount = _screenshots.Count;

            Mat baseScreen = _screenshots.Peek().Frame;
            Size size = baseScreen.Size();

            string path = Highlight.GetVideoPath(game, $"{Highlight.GetHighlightFileName(DateTime.Now)}.mp4");
            string directoryPath = Path.GetDirectoryName(path);

            //고정되지 않은 프레임 방식 (저장된 프레임의 수에 따라서 fps가 결정됨)
            if (isUnfixed) fps = (double) _screenshots.Count / (secondsBeforeHighlight + secondsAfterHighlight);

            if (File.Exists(path)) File.Delete(path);
            if (directoryPath != null && !Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            _videoWriter = new VideoWriter(path, FourCC.H265, fps, size);

            if (!_videoWriter.IsOpened())
            {
                _videoWriter.Release();
                baseScreen.Dispose();

                ExceptionManager.ShowError("하이라이트 영상을 저장할 수 없습니다.", "저장할 수 없음", nameof(Screen), nameof(Stop));
                return false;
            }

            ScreenCaptured lastScreen = null;

            while (_screenshots.Count > 0)
            {
                if (lastScreen == null || lastScreen.CountToUse == 0)
                {
                    lastScreen?.Frame?.Dispose(); //lastScreen 변수가 null이면 이 함수는 실행되지 않음
                    lastScreen = _screenshots.Dequeue();
                }

                if (lastScreen.Frame.Size() != size)
                {
                    lastScreen.Frame.Resize(size);
                }

                _videoWriter.Write(lastScreen.Frame);
                lastScreen.CountToUse--;
            }

            _videoWriter.Release();
            baseScreen.Dispose();
            _isWorking = false;

            return true;
        }
        #endregion
        #region Record Test Function
        public static async Task RecordTestForDebugAsync(int waitMilliseconds, double fps, int secondsBeforeHighlight, int secondsAfterHighlight)
        {
            int frameCount = 0;

            RecordForDebug(fps, secondsBeforeHighlight);
            await Task.Delay(waitMilliseconds);

            bool recorded = await Task.Run(() => StopForDebug(GameType.R6, fps, secondsBeforeHighlight, secondsAfterHighlight, out frameCount));
            MessageBox.Show($"OK : {recorded}, Count : {frameCount}");
        }
        #endregion
        #endregion

        public static Bitmap Screenshot()
        {
            return Screenshot(System.Drawing.Rectangle.Empty);
        }

        public static Bitmap Screenshot(System.Drawing.Rectangle bounds)
        {
            DesktopFrame desktopFrame = _dd.GetLatestFrame();
            return desktopFrame != null ? CropImage(desktopFrame.DesktopImage, bounds) : null;
        }

        public static Bitmap Screenshot(int x, int y, int width, int height)
        {
            return Screenshot(new System.Drawing.Rectangle(x, y, width, height));
        }

        /// <summary>
        /// 비트맵을 특정한 영역으로 자릅니다.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="rect"></param>
        /// <returns></returns>
        private static Bitmap CropImage(Bitmap image, System.Drawing.Rectangle rect)
        {
            if (rect == System.Drawing.Rectangle.Empty || (image != null && image.Width == rect.Width && image.Height == rect.Height)) return image;
            return image?.Clone(rect, image.PixelFormat);
        }

        public static async Task WorkForRecordingAsync()
        {
            while (!IsDisposed)
            {
                if (!IsRecording && !_isWorking)
                {
                    IGameClient activeClient = ClientManager.CurrentClient;
                    await Record(activeClient);
                }

                await Task.Delay(1000);
            }
        }

        public static void Dispose()
        {
            IsDisposed = true;

            ClearScreenshots();
            _screenshots = null;
        }

        /// <summary>
        /// 메모리에 있던 스크린샷들을 제거합니다.
        /// </summary>
        private static void ClearScreenshots()
        {
            while (_screenshots.Count > 0)
            {
                ScreenCaptured sc = _screenshots.Dequeue();
                sc.Frame.Dispose();
            }
        }
    }
}
