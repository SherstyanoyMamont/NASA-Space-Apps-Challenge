using Microsoft.VisualBasic.ApplicationServices;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;


namespace NASA_Space_Apps_Challenge {
    public partial class Form1 : Form {


        // GeoJSON bbox в системе координат WGS84(EPSG:4326).
        // Порядок : minLon, minLat, maxLon, maxLat = запад, юг, восток, север.
        // Единицы — десятичные градусы(точка как разделитель), диапазоны: lon[-180..180], lat[-90..90].

        // Пример(Нидерланды, небольшой бокс) :
        // [5.70, 52.60, 5.90, 52.75]


        private double minLon = 5.70;
        private double minLat = 52.60;
        private double maxLon = 5.90;
        private double maxLat = 52.75;

        /* EARTH LOGIN TOKEN */
        public readonly string EARTH_LOGIN_TOKEN =
                   "eyJ0eXAiOiJKV1QiLCJvcmlnaW4iOiJFYXJ0aGRhdGEgTG9naW4iLCJzaWciOiJlZGxqd3RwdWJrZXlfb3BzIiwiYWxnIjoiUlMyNTYifQ.eyJ0eXBlIjoiVXNlciIsInVpZCI6ImFzY2VnIiwiZXhwIjoxNzY0NzI0Mzg3LCJpYXQiOjE3NTk1NDAzODcsImlzcyI6Imh0dHBzOi8vdXJzLmVhcnRoZGF0YS5uYXNhLmdvdiIsImlkZW50aXR5X3Byb3ZpZGVyIjoiZWRsX29wcyIsImFjciI6ImVkbCIsImFzc3VyYW5jZV9sZXZlbCI6M30.zPuUBGNgjzzeIbXzQu2Tt2B5XSG-VWUfQa8229fjLzEcRIi2dGKKIqofs1oFksOuYIKHky43WD8y6oce8yrIi4YgV5_5yjdDpeL2T7SMdmYbZxMUsI1rCYFRUuz3IBO8bJCE9E3JPqY9wewtTmY0XxjVKTBRkCu-q8Okzh-VZkoUlypnGgF_RvbEKt6DFVIrLmkVhYslIJ_ipdngLNSgVVV-ZeKZ3uT--h7NdhPcryukNWY5hkQ8mlfOjdhWYgBTz0RFPsLkEh7Pf0Ynk1EE7QS-Q_w_kl-I_pBNkYmE4SglS4PF-5s0PgmfH6iKC0ST91UB5605L5gL2pa7gcUBAQ";

        public Form1() {
            InitializeComponent();
        }


        string AppeearsLogin = "asceg";
        string AppeearsPass = "In345-523-234Code";

        record HeatPoint(double lat, double lon, double weight);

        short[,] ReadTiff16(string path) {
            using var image = BitMiracle.LibTiff.Classic.Tiff.Open(path, "r");

            int width = image.GetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = image.GetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGELENGTH)[0].ToInt();

            int bps = image.GetField(BitMiracle.LibTiff.Classic.TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 16;
            int spp = image.GetField(BitMiracle.LibTiff.Classic.TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;
            int sf = image.GetField(BitMiracle.LibTiff.Classic.TiffTag.SAMPLEFORMAT)?[0].ToInt()
                      ?? (int)BitMiracle.LibTiff.Classic.SampleFormat.UINT;

            bool isFloat = sf == (int)BitMiracle.LibTiff.Classic.SampleFormat.IEEEFP;
            int bytesPerSample = Math.Max(1, bps / 8);
            int bytesPerPixel = bytesPerSample * Math.Max(1, spp);

            var arr = new short[height, width];
            var scanline = new byte[image.ScanlineSize()];

            for (int y = 0; y < height; y++) {
                image.ReadScanline(scanline, y);
                for (int x = 0; x < width; x++) {
                    int off = x * bytesPerPixel;
                    if (off + bytesPerSample > scanline.Length) break; // защита от выхода

                    short val;
                    if (isFloat && bytesPerSample == 4) {
                        float f = BitConverter.ToSingle(scanline, off);      // 0..1 (обычно)
                        int v = (int)Math.Round(f * 10000.0);
                        val = (short)Math.Clamp(v, 0, 10000);
                    }
                    else if (bytesPerSample == 2) {
                        ushort u = BitConverter.ToUInt16(scanline, off);
                        val = (short)u; // 0..65535, но HLS 0..10000
                    }
                    else if (bytesPerSample == 1) {
                        byte b = scanline[off];
                        val = (short)(b << 8);
                    }
                    else {
                        // Неподдержанный формат — запишем 0
                        val = 0;
                    }

                    arr[y, x] = val;
                }
            }
            return arr;
        }


        // weight из GRI и NDVI (0..1), игнорируем облака по Fmask
        List<HeatPoint> BuildHeat(string id) {
            string baseDir = Path.Combine("data", "clips");
            // ищем подходящую NIR: сначала B08 (S2), иначе B05 (Landsat)
            string b03 = Directory.GetFiles(baseDir, $"{id}_B03.tif").FirstOrDefault();
            string b04 = Directory.GetFiles(baseDir, $"{id}_B04.tif").FirstOrDefault();
            string b08 = Directory.GetFiles(baseDir, $"{id}_B08.tif").FirstOrDefault();
            string b05 = Directory.GetFiles(baseDir, $"{id}_B05.tif").FirstOrDefault();
            string fmsk = Directory.GetFiles(baseDir, $"{id}_Fmask.tif").FirstOrDefault();

            if (b03 == null || b04 == null || (b08 == null && b05 == null)) return new();

            var G = ReadTiff16(b03);
            var R = ReadTiff16(b04);
            var N = ReadTiff16(b08 ?? b05);
            short[,] F = fmsk != null ? ReadTiff16(fmsk) : null;

            int h = G.GetLength(0), w = G.GetLength(1);
            var pts = new List<HeatPoint>(w * h);

            // шаг пикселя по геометрии клипа:
            double lonStep = (maxLon - minLon) / w;
            double latStep = (maxLat - minLat) / h;

            for (int y = 0; y < h; y++) {
                double lat = maxLat - (y + 0.5) * latStep;
                for (int x = 0; x < w; x++) {
                    double lon = minLon + (x + 0.5) * lonStep;

                    if (F != null) {
                        // Fmask классы: 0=Clear, 1=Water, 2=Shadow, 3=Snow, 4=Cloud (варианты зависят от версии)
                        var c = F[y, x];
                        if (c == 4 || c == 2) continue; // облака/тени — пропускаем
                    }

                    // значения отражения масштабируем к [0..1] из [0..10000]
                    double g = Math.Clamp(G[y, x] / 10000.0, 0, 1);
                    double r = Math.Clamp(R[y, x] / 10000.0, 0, 1);
                    double n = Math.Clamp(N[y, x] / 10000.0, 0, 1);

                    double gri = (g + r > 1e-6) ? (g - r) / (g + r) : 0;   // -1..1
                    double ndvi = (n + r > 1e-6) ? (n - r) / (n + r) : 0;   // -1..1

                    // грубо нормируем в 0..1 и перемножаем как «интенсивность цветения»
                    double gri01 = (gri + 1) * 0.5;
                    double ndvi01 = (ndvi + 1) * 0.5;

                    // базовый вес
                    double wgt = gri01 * ndvi01;

                    // фильтр: не берём совсем тёмные/почти нулевые пиксели
                    if (double.IsFinite(wgt) && wgt > 0.05)
                        pts.Add(new HeatPoint(lat, lon, Math.Clamp(wgt, 0, 1)));
                }
            }
            return pts;
        }

        string ToHeatmapJson(IEnumerable<HeatPoint> pts) {
            var arr = pts.Select(p => new {
                location = new { lat = p.lat, lng = p.lon },
                weight = Math.Round(p.weight, 3)
            });
            return System.Text.Json.JsonSerializer.Serialize(arr);
        }

        // 1) кнопка
        // Ищем, где лежит proj.db (поддерживаем обе раскладки из GISInternals)
        string ResolveProjDir(string gdalRoot) {
            var candidates = new[]
            {
        Path.Combine(gdalRoot, "share", "proj"),
        Path.Combine(gdalRoot, "share", "proj9"),
        Path.Combine(gdalRoot, "bin",   "proj",  "share"),
        Path.Combine(gdalRoot, "bin",   "proj9", "share"),
    };
            foreach (var dir in candidates)
                if (System.IO.File.Exists(Path.Combine(dir, "proj.db")))
                    return dir;

            throw new Exception(
                $"Не найден proj.db ни в одном из кандидатов:\n{string.Join("\n", candidates)}");
        }

        // Ищем GDAL-data(у GISInternals чаще всего bin\gdal-data, но бывает share\gdal)
        string ResolveGdalDataDir(string gdalRoot) {
            var candidates = new[]
            {
                Path.Combine(gdalRoot, "bin",   "gdal-data"),
                Path.Combine(gdalRoot, "share", "gdal"),
            };
            foreach (var dir in candidates)
                if (Directory.Exists(dir))
                    return dir;

            throw new Exception(
                $"Не найден каталог GDAL_DATA ни в одном из кандидатов:\n{string.Join("\n", candidates)}");
        }


        public async Task<string> RunGdalWarpAsync(
            string gdalRoot, string exePath, string url, string outTif,
            double minLon, double minLat, double maxLon, double maxLat,
            string earthToken, int outWidth = 512) {
            string binDir = Path.Combine(gdalRoot, "bin");
            string gdalData = ResolveGdalDataDir(gdalRoot);
            string projDir = ResolveProjDir(gdalRoot);

            // 1) скачиваем защищённый TIFF локально
            string srcLocal = await DownloadWithBearerAsync(url, earthToken);

            try {
                var args =
                    $"--config GDAL_DATA \"{gdalData}\" " +
                    $"--config PROJ_LIB  \"{projDir}\" " +
                    // (headers уже не нужны — источник локальный)
                    $"--config GDAL_DISABLE_READDIR_ON_OPEN YES " +
                    $"--config VSI_CACHE TRUE " +
                    $"-t_srs EPSG:4326 -te_srs EPSG:4326 -te {minLon} {minLat} {maxLon} {maxLat} " +
                    $"-ts {outWidth} 0 -r bilinear -multi -wo NUM_THREADS=ALL_CPUS " +
                    $"-of GTiff " +
                    $@"""{srcLocal}"" ""{outTif}""";

                var psi = new System.Diagnostics.ProcessStartInfo(exePath, args) {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = binDir
                };
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.Environment["PATH"] = binDir + ";" + path;
                psi.Environment["PROJ_LIB"] = projDir;
                psi.Environment["GDAL_DATA"] = gdalData;

                using var p = System.Diagnostics.Process.Start(psi)!;
                var std = p.StandardOutput.ReadToEnd();
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) throw new Exception(err);

                return outTif;
            }
            finally {
                try { System.IO.File.Delete(srcLocal); } catch { /* ignore */ }
            }
        }



        string ResolveGdalApp(string gdalRoot, string appExe) {
            var candidates = new[]
            {
        Path.Combine(gdalRoot, "gdal", "apps", appExe),
        Path.Combine(gdalRoot, "bin",  "gdal", "apps", appExe),
        Path.Combine(gdalRoot, "bin",  appExe),
    };
            var found = candidates.FirstOrDefault(System.IO.File.Exists);
            if (found == null)
                throw new FileNotFoundException($"{appExe} not found", string.Join(Environment.NewLine, candidates));
            return found;
        }



        private async void button1_Click(object sender, EventArgs e) {
            try {
                // 1) STAC-поиск HLS (HLSS30 + HLSL30) в окне дат
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", EARTH_LOGIN_TOKEN);

                var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
                var endExcl = new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc);

                var body = new {
                    collections = new[] { "HLSS30.v2.0", "HLSL30.v2.0" },
                    bbox = new[] { minLon, minLat, maxLon, maxLat },
                    datetime = $"{start:yyyy-MM-ddTHH:mm:ssZ}/{endExcl:yyyy-MM-ddTHH:mm:ssZ}",
                    limit = 200
                };
                var req = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var resp = await http.PostAsync("https://cmr.earthdata.nasa.gov/stac/LPCLOUD/search", req);
                var respText = await resp.Content.ReadAsStringAsync();
                resp.EnsureSuccessStatusCode();

                var hrefs = new List<(string id, string url, string band)>();
                using (var doc = JsonDocument.Parse(respText)) {
                    foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray()) {
                        var id = f.GetProperty("id").GetString()!;
                        var assets = f.GetProperty("assets");
                        foreach (var band in new[] { "B03", "B04", "B08", "B05", "Fmask" }) {
                            if (assets.TryGetProperty(band, out var a) && a.TryGetProperty("href", out var href))
                                hrefs.Add((id, href.GetString()!, band));
                        }
                    }
                }

                if (hrefs.Count == 0) {
                    MessageBox.Show("STAC: не найдено ни одной сцены/полосы для заданного bbox/дат.");
                    return;
                }

                // 2) Клипим несколько сцен (B03/B04/NIR/Fmask) — файлы появятся в data\clips\
                await ClipFewScenesAsync(hrefs);

                var clipsDir = Path.Combine("data", "clips");
                var tifCount = Directory.Exists(clipsDir)
                    ? Directory.GetFiles(clipsDir, "*.tif").Length
                    : 0;

                // 3) Для первой сцены считаем heat-точки и сохраняем json (проверка пайплайна)
                var firstId = hrefs.Select(h => h.id).First();
                var heatPts = BuildHeat(firstId);
                var heatJsonPath = Path.Combine(clipsDir, $"{firstId}_heat.json");
                System.IO.File.WriteAllText(heatJsonPath, ToHeatmapJson(heatPts));

                MessageBox.Show(
                    $"Готово.\nКлипов: {tifCount}\nПервая сцена: {firstId}\nHeat points: {heatPts.Count}\nJSON: {heatJsonPath}");
            }
            catch (Exception ex) {
                MessageBox.Show("Ошибка теста клипов:\n" + ex);
            }
        }


        async Task<string> DownloadWithBearerAsync(string url, string token) {
            string tmp = Path.Combine(Path.GetTempPath(), "hls_" + Guid.NewGuid().ToString("N") + ".tif");
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All });
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = System.IO.File.Create(tmp);
            await resp.Content.CopyToAsync(fs);
            return tmp; // не удаляем сразу – удалим после warp/ошибки
        }


        public async Task ClipFewScenesAsync(IEnumerable<(string id, string url, string band)> assets) {
            Directory.CreateDirectory(Path.Combine("data", "clips"));
            var gdalRoot = Path.Combine(AppContext.BaseDirectory, "release-1930-x64-gdal-3-11-3-mapserver-8-4-0");
            var sem = new SemaphoreSlim(3);
            var clipsRoot = Path.Combine(AppContext.BaseDirectory, "data", "clips");
            Directory.CreateDirectory(clipsRoot);
            var wanted = assets
                .GroupBy(a => a.id)
                .Take(4)
                .SelectMany(g => g.Where(a => a.band is "B03" or "B04" or "B08" or "B05" or "Fmask"));

            var warpExe = ResolveGdalApp(gdalRoot, "gdalwarp.exe");

            var tasks = wanted.Select(async a =>
            {
                await sem.WaitAsync();
                try {
                    var fileName = $"{a.id}_{a.band}.tif"
                        .Replace('/', '_').Replace('\\', '_'); // на всякий случай

                    var outTifAbs = Path.Combine(clipsRoot, fileName);  // абсолютный путь

                    if (!System.IO.File.Exists(outTifAbs))
                        await RunGdalWarpAsync(
                            gdalRoot, warpExe,
                            a.url, outTifAbs,           // <-- абсолютный
                            minLon, minLat, maxLon, maxLat,
                            EARTH_LOGIN_TOKEN, 256);
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);

           
        }



    }

}
