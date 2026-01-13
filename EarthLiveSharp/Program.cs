using System;
using System.Windows.Forms;
using System.Net;
using System.Net.Cache;  
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Drawing;

namespace EarthLiveSharp
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args) 
        {
            if (System.Environment.OSVersion.Version.Major >= 6) { SetProcessDPIAware(); }
            if (File.Exists(Application.StartupPath + @"\trace.log"))
            {
                File.Delete(Application.StartupPath + @"\trace.log");
            }
            Trace.Listeners.Add(new TextWriterTraceListener(Application.StartupPath + @"\trace.log"));
            Trace.AutoFlush = true;

            try
            {
                Cfg.Load();
            }
            catch
            {
                return;
            }
            if (Cfg.source_selection ==0 & Cfg.cloud_name.Equals("demo"))
            {
                #if DEBUG

                #else
                DialogResult dr = MessageBox.Show("WARNING: it's recommended to get images from CDN. \n 注意：推荐使用CDN方式来抓取图片，以提高稳定性。", "EarthLiveSharp");
                if (dr == DialogResult.OK)
                {
                    Process.Start("https://github.com/bitdust/EarthLiveSharp/issues/32");
                }
                #endif
            }
            //if (Cfg.language.Equals("en")| Cfg.language.Equals("zh-Hans")| Cfg.language.Equals("zh-Hant"))
            //{
            //    System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(Cfg.language);
            //}
            Cfg.image_folder = Application.StartupPath + @"\images";
            Cfg.Save();
            Scrap_wrapper.set_scraper();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new mainForm());
        }
    }

    public static class Scrap_wrapper
    {
        public static int SequenceCount = 0;
        private static IScraper scraper;
        public static void set_scraper()
        {
            scraper = new Scraper_himawari8();
            return;
        }
        public static void UpdateImage()
        {
            scraper.UpdateImage();
        }

        public static void ResetState()
        {
            scraper.ResetState();
        }

        public static void CleanCDN()
        {
            scraper.CleanCDN();
        }
    }

    interface IScraper
    {
        void UpdateImage();
        void CleanCDN();
        void ResetState();
    }
    public class Scraper_himawari8 : IScraper

    // // New URL from JMA
    // {
    //     // JMA URL Pattern: https://www.data.jma.go.jp/mscweb/data/himawari/img/aus/aus_tre_HHmm.jpg
    //     private const string JMA_BASE_URL = "https://www.data.jma.go.jp/mscweb/data/himawari/img/aus/aus_tre_";

    //     public void UpdateImage()
    //     {
    //         InitFolder();

    //         try
    //         {
    //             // 1. Get current UTC time
    //             DateTime nowUtc = DateTime.UtcNow;

    //             // 2. Subtract 10 minutes to avoid fetching an image that hasn't been uploaded yet.
    //             //    (This also handles the "going back one day" logic automatically via DateTime math)
    //             DateTime adjustedTime = nowUtc.AddMinutes(-10);

    //             // 3. Round down to the nearest 10-minute interval
    //             //    Example: 00:19 -> 00:10,  00:05 -> 00:00
    //             int minute = adjustedTime.Minute;
    //             int roundedMinute = (minute / 10) * 10;

    //             // 4. Construct the target timestamp
    //             DateTime targetTime = new DateTime(
    //                 adjustedTime.Year, 
    //                 adjustedTime.Month, 
    //                 adjustedTime.Day, 
    //                 adjustedTime.Hour, 
    //                 roundedMinute, 
    //                 0, 
    //                 DateTimeKind.Utc
    //             );

    //             // 5. Generate the URL (Format: HHmm, e.g., "2350", "0010")
    //             string timeStr = targetTime.ToString("HHmm");
    //             string imageUrl = JMA_BASE_URL + timeStr + ".jpg";
    //             string savePath = Cfg.image_folder + "\\wallpaper.bmp";

    //             // 6. Download
    //             // Clean up old file first
    //             if (File.Exists(savePath))
    //             {
    //                 File.Delete(savePath);
    //             }

    //             using (WebClient client = new WebClient())
    //             {
    //                 // Tls12 is required for most government websites now
    //                 ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

    //                 Trace.WriteLine("[JMA Download] Requesting: " + imageUrl);
    //                 client.DownloadFile(imageUrl, savePath);
    //                 Trace.WriteLine("[JMA Download] Success. Saved to " + savePath);
    //             }
    //         }
    //         catch (Exception e)
    //         {
    //             Trace.WriteLine("[JMA Download Error] " + e.Message);
    //         }
    //     }

    //     private void InitFolder()
    //     {
    //         if (!Directory.Exists(Cfg.image_folder))
    //         {
    //             Directory.CreateDirectory(Cfg.image_folder);
    //         }
    //     }

    //     // --- Interface Stubs (Not needed for direct JMA download) ---
    //     public void CleanCDN()
    //     {
    //         // Logic removed as we are not using Cloudinary/CDN for this source
    //     }

    //     public void ResetState()
    //     {
    //         // No state tracking needed for direct time-based download
    //     }
    // }

    // Old url from NICT
    {
        private string imageID = "";
        private static string last_imageID = "0";
        private string json_url = "https://himawari8-dl.nict.go.jp/himawari8/img/FULL_24h/latest.json";

        private int GetImageID()
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(json_url) as HttpWebRequest;
            try 
            {
                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("[himawari8 connection error]");
                }
                if (!response.ContentType.Contains("application/json"))
                {
                    throw new Exception("[himawari8 no json recieved. your Internet connection is hijacked]");
                }
                StreamReader reader = new StreamReader(response.GetResponseStream());
                string date = reader.ReadToEnd();
                imageID = date.Substring(9,19).Replace("-", "/").Replace(" ", "/").Replace(":", "");
                Trace.WriteLine("[himawari8 get latest ImageID] " + imageID);
                reader.Close();
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
                return -1;
            }
            return 0;
        }

        private int SaveImage()
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            WebClient client = new WebClient();
            string image_source = "";
            if (Cfg.source_selection == 1)
            {
               image_source = "https://res.cloudinary.com/" + Cfg.cloud_name + "/image/fetch/https://himawari8-dl.nict.go.jp/himawari8/img/D531106";
            }
            else
            {
               image_source = "https://himawari8-dl.nict.go.jp/himawari8/img/D531106";
            }
            string url = "";
            string image_path = "";
            try
            {
                for (int ii = 0; ii < Cfg.size; ii++)
                {
                    for (int jj = 0; jj < Cfg.size; jj++)
                    {
                        url = string.Format("{0}/{1}d/550/{2}_{3}_{4}.png", image_source, Cfg.size, imageID, ii, jj);
                        image_path = string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj); // remove the '/' in imageID
                        client.DownloadFile(url, image_path);
                    }
                }
                Trace.WriteLine("[save image] " + imageID);
                return 0;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message + " " + imageID);
                Trace.WriteLine(string.Format("[url]{0} [image_path]{1}", url, image_path));
                return -1;
            }
            finally
            {
                client.Dispose();
            }
        }

        // Original FullDisk JoinImage
        // private void JoinImage()
        // {
        //     // join & convert the images to wallpaper.bmp
        //     Bitmap bitmap = new Bitmap(550 * Cfg.size, 550 * Cfg.size);
        //     Image[,] tile = new Image[Cfg.size, Cfg.size];
        //     Graphics g = Graphics.FromImage(bitmap);
        //     for (int ii = 0; ii < Cfg.size; ii++)
        //     {
        //         for (int jj = 0; jj < Cfg.size; jj++)
        //         {
        //             tile[ii,jj] = Image.FromFile(string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj));
        //             g.DrawImage(tile[ii, jj], 550 * ii, 550 * jj);
        //             tile[ii, jj].Dispose();
        //         }
        //     }
        //     g.Save();
        //     g.Dispose();
        //     if (Cfg.zoom == 100)
        //     {
        //         bitmap.Save(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder),System.Drawing.Imaging.ImageFormat.Bmp);
        //     }
        //     else if (1 < Cfg.zoom & Cfg.zoom <100)
        //     {
        //         int new_size = bitmap.Height * Cfg.zoom /100;
        //         Bitmap zoom_bitmap = new Bitmap(new_size, new_size);
        //         Graphics g_2 = Graphics.FromImage(zoom_bitmap);
        //         g_2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        //         g_2.DrawImage(bitmap, 0, 0, new_size, new_size);
        //         g_2.Save();
        //         g_2.Dispose();
        //         zoom_bitmap.Save(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder),System.Drawing.Imaging.ImageFormat.Bmp);
        //         zoom_bitmap.Dispose();
        //     }
        //     else
        //     {
        //         Trace.WriteLine("[himawari8 zoom error]");
        //     }

        //     bitmap.Dispose();

        //     if (Cfg.saveTexture && Cfg.saveDirectory != "selected Directory")
        //     {
        //         if (Scrap_wrapper.SequenceCount >= Cfg.saveMaxCount)
        //         {
        //             Scrap_wrapper.SequenceCount = 0;
        //         }
        //         try
        //         {
        //             File.Copy(string.Format("{0}\\wallpaper.bmp", Cfg.image_folder), Cfg.saveDirectory + "\\" + "wallpaper_" + Scrap_wrapper.SequenceCount + ".bmp", true);
        //             Scrap_wrapper.SequenceCount++;
        //         }
        //         catch (Exception e)
        //         {
        //             Trace.WriteLine("[can't save wallpaper to distDirectory]");
        //             Trace.WriteLine(e.Message);
        //             return;
        //         }
        //     }
        // }

        // New Australia focused JoinImage
        private void JoinImage()
        {
            // 1. Setup dimensions for the Full Disk
            int fullWidth = 550 * Cfg.size;
            int fullHeight = 550 * Cfg.size;

            // 2. Create the full disk in memory first
            using (Bitmap fullDisk = new Bitmap(fullWidth, fullHeight))
            {
                using (Graphics g = Graphics.FromImage(fullDisk))
                {
                    // Draw all downloaded tiles onto the full disk canvas
                    for (int ii = 0; ii < Cfg.size; ii++)
                    {
                        for (int jj = 0; jj < Cfg.size; jj++)
                        {
                            string tilePath = string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj);
                            if (File.Exists(tilePath))
                            {
                                using (Image tile = Image.FromFile(tilePath))
                                {
                                    g.DrawImage(tile, 550 * ii, 550 * jj);
                                }
                            }
                        }
                    }
                }

                // 3. DEFINE CROP: Focus on Australia / Bass Strait / East Coast
                // Himawari-8 Center is 140.7E (approx. mid-Australia longitude).
                // 
                // Settings below are percentages (0.0 to 1.0) of the full image.
                // Adjust these numbers to zoom in tighter or pan around.
                
                // X=0.25: Starts a bit West of WA.
                // Y=0.55: Starts around Northern Territory (south of Equator).
                // W=0.45: Wide enough to include NZ.
                // H=0.35: Tall enough to include Tasmania/Bass Strait and Southern Ocean.

                int cropX = (int)(fullWidth * 0.25);
                int cropY = (int)(fullHeight * 0.55);
                int cropW = (int)(fullWidth * 0.45);
                int cropH = (int)(fullHeight * 0.35);

                // Safety check to prevent crashing if bounds are wrong
                if (cropX + cropW > fullWidth) cropW = fullWidth - cropX;
                if (cropY + cropH > fullHeight) cropH = fullHeight - cropY;

                Rectangle cropRect = new Rectangle(cropX, cropY, cropW, cropH);

                // 4. Crop the image and Save as wallpaper.bmp
                // We use Clone to extract the specific rectangle
                using (Bitmap croppedBitmap = fullDisk.Clone(cropRect, fullDisk.PixelFormat))
                {
                    // Save the Main Wallpaper
                    string wallpaperPath = string.Format("{0}\\wallpaper.bmp", Cfg.image_folder);
                    croppedBitmap.Save(wallpaperPath, System.Drawing.Imaging.ImageFormat.Bmp);

                    // 5. Save to Archive Directory (if enabled in settings)
                    if (Cfg.saveTexture && Cfg.saveDirectory != "selected Directory")
                    {
                        if (Scrap_wrapper.SequenceCount >= Cfg.saveMaxCount)
                        {
                            Scrap_wrapper.SequenceCount = 0;
                        }
                        try
                        {
                            string archivePath = Cfg.saveDirectory + "\\" + "wallpaper_" + Scrap_wrapper.SequenceCount + ".bmp";
                            croppedBitmap.Save(archivePath, System.Drawing.Imaging.ImageFormat.Bmp);
                            Scrap_wrapper.SequenceCount++;
                        }
                        catch (Exception e)
                        {
                            Trace.WriteLine("[can't save wallpaper to distDirectory]");
                            Trace.WriteLine(e.Message);
                        }
                    }
                }
            }
            // 'using' blocks automatically dispose of fullDisk to free memory
        }

        private void InitFolder()
        {
            if(Directory.Exists(Cfg.image_folder))
            {
                // delete all images in the image folder.
                //string[] files = Directory.GetFiles(image_folder);
                //foreach (string fn in files)
                //{
                //    File.Delete(fn);
                //}
            }
            else
            {
                Trace.WriteLine("[himawari8 create folder]");
                Directory.CreateDirectory(Cfg.image_folder);
            }
        }
        public void UpdateImage()
        {
            InitFolder();
            if (GetImageID() == -1)
            {
                return;
            }
            if (imageID.Equals(last_imageID))
            {
                return;
            }
            if (SaveImage()==0)
            {
                JoinImage();
            }
            last_imageID = imageID;
            return;
        }
        public void CleanCDN()
        {
            Cfg.Load();
            if (Cfg.api_key.Length == 0) return;
            if (Cfg.api_secret.Length == 0) return;
            try
            {
                HttpWebRequest request = WebRequest.Create("https://api.cloudinary.com/v1_1/" + Cfg.cloud_name + "/resources/image/fetch?prefix=https://himawari8-dl") as HttpWebRequest;
                request.Method = "DELETE";
                request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                string svcCredentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(Cfg.api_key + ":" + Cfg.api_secret));
                request.Headers.Add("Authorization", "Basic " + svcCredentials);
                HttpWebResponse response = null;
                StreamReader reader = null;
                string result = null;
                for (int i = 0; i < 3;i++ ) // max 3 request each hour.
                {
                    response = request.GetResponse() as HttpWebResponse;
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception("[himawari8 clean CND cache connection error]");
                    }
                    if (!response.ContentType.Contains("application/json"))
                    {
                        throw new Exception("[himawari8 clean CND cache no json recieved. your Internet connection is hijacked]");
                    }
                    reader = new StreamReader(response.GetResponseStream());
                    result = reader.ReadToEnd();
                    if (result.Contains("\"error\""))
                    {
                        throw new Exception("[himawari8 clean CND cache request error]\n" + result);
                    }
                    if (result.Contains("\"partial\":false"))
                    {
                        Trace.WriteLine("[himawari8 clean CDN cache done]");
                        break; // end of Clean CDN
                    }
                    else
                    {
                        Trace.WriteLine("[himawari8 more images to delete]");
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("[himawari8 error when delete CDN cache]");
                Trace.WriteLine(e.Message);
                return;
            }
        }
        public void ResetState()
        {
            last_imageID = "0";
        }
    }

    public static class Autostart
    {
        static string key = "EarthLiveSharp";
        public static bool Set(bool enabled)
        {
            RegistryKey runKey = null;
            try
            {
                string path = Application.ExecutablePath;
                runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (enabled)
                {
                    runKey.SetValue(key, path);
                }
                else
                {
                    runKey.SetValue(key, path); // dirty fix: to avoid exception in next line.
                    runKey.DeleteValue(key);
                }
                return true;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e.Message);
                return false;
            }
            finally
            {
                if(runKey!=null)
                {
                    runKey.Close();
                }
            }
        }
    }
}
