using System;
using System.Windows.Forms;
using System.Net;
using System.Net.Cache;   
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;

namespace EarthLiveSharp
{
    // Helper struct for City Data
    public struct City
    {
        public string Name;
        public double Lat;
        public double Lon;
        public City(string name, double lat, double lon)
        {
            Name = name;
            Lat = lat;
            Lon = lon;
        }
    }

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
            if (Cfg.source_selection == 0 & Cfg.cloud_name.Equals("demo"))
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
    {
        private string imageID = "";
        private static string last_imageID = "0";
        private string json_url = "https://himawari8-dl.nict.go.jp/himawari8/img/FULL_24h/latest.json";

        // Cities List
        private List<City> cities = new List<City>()
        {
            new City("Shanghai", 31.23, 121.47),
            new City("Sydney", -33.86, 151.21),
            new City("Adelaide", -34.93, 138.60),
            new City("Perth", -31.95, 115.86),
            new City("Brisbane", -27.47, 153.03),
            new City("Melbourne", -37.81, 144.96)
        };

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
                        image_path = string.Format("{0}\\{1}_{2}.png", Cfg.image_folder, ii, jj); 
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

        // --- FIXED PROJECTION GEOMETRY ---
        private Point GetPixelCoordinates(double lat, double lon, int width, int height)
        {
            // Himawari-8/9 Sub-satellite Point
            double satLon = 140.7; 
            
            double radLat = lat * Math.PI / 180.0;
            double radLon = lon * Math.PI / 180.0;
            double radSatLon = satLon * Math.PI / 180.0;
            double radLonDiff = radLon - radSatLon;

            // Perspective constants
            // P = distance_satellite / radius_earth = 42164 / 6378
            double P = 6.610689; 
            
            // Geocentric coords
            double x_g = Math.Cos(radLat) * Math.Cos(radLonDiff);
            double y_g = Math.Cos(radLat) * Math.Sin(radLonDiff);
            double z_g = Math.Sin(radLat);

            // Visibility check (hide if on the back side)
            if (x_g < 0.155) // approximate limb limit
            {
                return new Point(-100, -100);
            }

            // Vertical Perspective Projection (View from P looking at 0)
            // The mapping factor K maps the geometric projection to the normalized plane
            double K = (P - 1) / (P - x_g);
            
            double x_proj = K * y_g; 
            double y_proj = K * z_g;

            // CALIBRATION FIX:
            // At the limb (edge of earth), x_proj is approx 0.858.
            // We want the limb to be at the edge of the image (0.5 coordinates).
            // Scale = 0.5 / 0.858 = ~0.582
            double scale = 0.582; 

            int pixelX = (int)((0.5 + x_proj * scale) * width);
            int pixelY = (int)((0.5 - y_proj * scale) * height);

            return new Point(pixelX, pixelY);
        }

        private void DrawCityMarkers(Bitmap bmp)
        {
            // Dynamic Sizing
            int refSize = bmp.Width; 
            int fontSize = Math.Max(14, refSize / 65); 
            int dotSize = Math.Max(8, refSize / 180); 
            int padding = dotSize / 2;

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                using (Brush brushRed = new SolidBrush(Color.Red))
                using (Pen penWhite = new Pen(Color.White, 2))
                using (Font font = new Font("Arial", fontSize, FontStyle.Bold))
                using (GraphicsPath path = new GraphicsPath())
                {
                    foreach (var city in cities)
                    {
                        Point p = GetPixelCoordinates(city.Lat, city.Lon, bmp.Width, bmp.Height);

                        // Skip if off-screen (e.g. hidden side)
                        if (p.X < 0 || p.Y < 0) continue;

                        // Draw Dot
                        Rectangle dotRect = new Rectangle(p.X - padding, p.Y - padding, dotSize, dotSize);
                        g.FillEllipse(brushRed, dotRect);
                        g.DrawEllipse(penWhite, dotRect);

                        // Draw Text with Outline
                        string text = city.Name;
                        PointF textPos = new PointF(p.X + dotSize, p.Y - (fontSize / 2));

                        path.Reset();
                        path.AddString(text, font.FontFamily, (int)font.Style, font.Size, textPos, StringFormat.GenericDefault);
                        
                        g.DrawPath(new Pen(Color.Black, 3), path);
                        g.FillPath(Brushes.White, path);
                    }
                }
            }
        }

        private void JoinImage()
        {
            int fullWidth = 550 * Cfg.size;
            int fullHeight = 550 * Cfg.size;

            using (Bitmap fullDisk = new Bitmap(fullWidth, fullHeight))
            {
                // 1. Reconstruct Full Disk
                using (Graphics g = Graphics.FromImage(fullDisk))
                {
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

                // 2. MARK CITIES on the Full Disk
                DrawCityMarkers(fullDisk);

                // 3. Prepare Crops
                // Crop A: Australia (Main)
                int auX = (int)(fullWidth * 0.25);
                int auY = (int)(fullHeight * 0.55);
                int auW = (int)(fullWidth * 0.45);
                int auH = (int)(fullHeight * 0.35);
                if (auX + auW > fullWidth) auW = fullWidth - auX;
                if (auY + auH > fullHeight) auH = fullHeight - auY;
                Rectangle rectAu = new Rectangle(auX, auY, auW, auH);

                // Crop B: Shanghai Area (Inset)
                int shX = (int)(fullWidth * 0.15); 
                int shY = (int)(fullHeight * 0.15); 
                int shW = (int)(fullWidth * 0.30); 
                int shH = (int)(fullHeight * 0.30); 
                if (shX + shW > fullWidth) shW = fullWidth - shX;
                if (shY + shH > fullHeight) shH = fullHeight - shY;
                Rectangle rectSh = new Rectangle(shX, shY, shW, shH);

                // 4. Composition
                using (Bitmap wallpaper = fullDisk.Clone(rectAu, fullDisk.PixelFormat))
                {
                    using (Bitmap shanghaiImg = fullDisk.Clone(rectSh, fullDisk.PixelFormat))
                    {
                        // Resize Shanghai Inset to 35% of wallpaper width
                        int insetWidth = (int)(wallpaper.Width * 0.35);
                        int insetHeight = (int)((double)shanghaiImg.Height / shanghaiImg.Width * insetWidth);
                        
                        using (Bitmap shanghaiSmall = new Bitmap(insetWidth, insetHeight))
                        {
                            using (Graphics gSmall = Graphics.FromImage(shanghaiSmall))
                            {
                                gSmall.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                gSmall.DrawImage(shanghaiImg, 0, 0, insetWidth, insetHeight);
                            }

                            // Draw Inset
                            using (Graphics gFinal = Graphics.FromImage(wallpaper))
                            {
                                int padding = 20;
                                // White border around inset
                                gFinal.DrawRectangle(new Pen(Color.White, 3), padding, padding, insetWidth, insetHeight);
                                gFinal.DrawImage(shanghaiSmall, padding, padding, insetWidth, insetHeight);
                            }
                        }
                    }

                    // Save
                    string wallpaperPath = string.Format("{0}\\wallpaper.bmp", Cfg.image_folder);
                    wallpaper.Save(wallpaperPath, System.Drawing.Imaging.ImageFormat.Bmp);

                    if (Cfg.saveTexture && Cfg.saveDirectory != "selected Directory")
                    {
                        if (Scrap_wrapper.SequenceCount >= Cfg.saveMaxCount)
                        {
                            Scrap_wrapper.SequenceCount = 0;
                        }
                        try
                        {
                            string archivePath = Cfg.saveDirectory + "\\" + "wallpaper_" + Scrap_wrapper.SequenceCount + ".bmp";
                            wallpaper.Save(archivePath, System.Drawing.Imaging.ImageFormat.Bmp);
                            Scrap_wrapper.SequenceCount++;
                        }
                        catch (Exception e)
                        {
                            Trace.WriteLine("[can't save wallpaper]");
                            Trace.WriteLine(e.Message);
                        }
                    }
                }
            }
        }

        private void InitFolder()
        {
            if(!Directory.Exists(Cfg.image_folder))
            {
                Directory.CreateDirectory(Cfg.image_folder);
            }
        }
        public void UpdateImage()
        {
            InitFolder();
            if (GetImageID() == -1) return;
            if (imageID.Equals(last_imageID)) return;
            if (SaveImage()==0)
            {
                JoinImage();
            }
            last_imageID = imageID;
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
                for (int i = 0; i < 3;i++ ) 
                {
                    response = request.GetResponse() as HttpWebResponse;
                    if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Error");
                    reader = new StreamReader(response.GetResponseStream());
                    result = reader.ReadToEnd();
                    if (result.Contains("\"partial\":false")) break; 
                }
            }
            catch (Exception e) { Trace.WriteLine(e.Message); }
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
                    runKey.SetValue(key, path); 
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