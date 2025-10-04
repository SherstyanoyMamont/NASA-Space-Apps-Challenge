using Microsoft.VisualBasic.ApplicationServices;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;


namespace NASA_Space_Apps_Challenge {
    public partial class LocalBloomForm : Form {



        private readonly double HEATMAP_ALPHA_SCALE = 1.0;
        private readonly double NDVI_MIN = 0.20; // нижний порог “растительности”
        private readonly double GAMMA = 1.20;        // >1 — красное позже, <1 — раньше
        private readonly double PLO = 5, PHI = 95; // перцентильный клип для стабильного контраста



        private readonly string EARTH_LOGIN_TOKEN =
                   "eyJ0eXAiOiJKV1QiLCJvcmlnaW4iOiJFYXJ0aGRhdGEgTG9naW4iLCJzaWciOiJlZGxqd3RwdWJrZXlfb3BzIiwiYWxnIjoiUlMyNTYifQ.eyJ0eXBlIjoiVXNlciIsInVpZCI6ImFzY2VnIiwiZXhwIjoxNzY0NzI0Mzg3LCJpYXQiOjE3NTk1NDAzODcsImlzcyI6Imh0dHBzOi8vdXJzLmVhcnRoZGF0YS5uYXNhLmdvdiIsImlkZW50aXR5X3Byb3ZpZGVyIjoiZWRsX29wcyIsImFjciI6ImVkbCIsImFzc3VyYW5jZV9sZXZlbCI6M30.zPuUBGNgjzzeIbXzQu2Tt2B5XSG-VWUfQa8229fjLzEcRIi2dGKKIqofs1oFksOuYIKHky43WD8y6oce8yrIi4YgV5_5yjdDpeL2T7SMdmYbZxMUsI1rCYFRUuz3IBO8bJCE9E3JPqY9wewtTmY0XxjVKTBRkCu-q8Okzh-VZkoUlypnGgF_RvbEKt6DFVIrLmkVhYslIJ_ipdngLNSgVVV-ZeKZ3uT--h7NdhPcryukNWY5hkQ8mlfOjdhWYgBTz0RFPsLkEh7Pf0Ynk1EE7QS-Q_w_kl-I_pBNkYmE4SglS4PF-5s0PgmfH6iKC0ST91UB5605L5gL2pa7gcUBAQ";
        private readonly string GOOGLE_API_KEY =
            "AIzaSyC_mRdsNwDiZSkVRIccqExl3WjVFT5ounc";

        private record HeatPoint(double lat, double lon, double weight);

        private string MapHtml(string apiKey) {

            return $@"
            <!doctype html><html><head><meta charset='utf-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1'>
            <style>
              html,body,#map{{height:100%;margin:0;padding:0}}
              .ctl{{position:absolute;top:12px;left:12px;z-index:5;
                    background:rgba(32,32,32,.65);color:#fff;border-radius:10px;
                    padding:10px 12px;backdrop-filter:saturate(1.2) blur(2px);
                    font:14px/1.2 system-ui,Segoe UI,Roboto,Helvetica,Arial}}
              .ctl input[type=range]{{width:220px}}
              .ctl .val{{display:inline-block;min-width:36px;text-align:right;margin-left:6px}}
            </style>
            <script src='https://maps.googleapis.com/maps/api/js?key={apiKey}&libraries=visualization&language=en'></script>
            </head>

            <div style=""position:absolute;right:12px;bottom:12px;z-index:5;
                        padding:6px 10px;border-radius:8px;background:rgba(32,32,32,.6);color:#fff;
                        font:12px system-ui"">
              <div style=""margin-bottom:4px"">Bloom intensity (low→high):</div>
              <div style=""width:200px;height:10px;
                          background:linear-gradient(90deg,
                            #12206e 0%, #1950c8 15%, #1ec0dc 30%, #28aa5a 45%,
                            #c8d23c 60%, #f0a83c 75%, #eb503c 90%, #b4141e 100%);
                          border-radius:4px""></div>
            </div>


            <body>
            <div id='map'></div>

            <div class='ctl'>
              <label>Bloom map opacity:
                <input id='op' type='range' min='0' max='1' step='0.01' value='0.55'>
                <span id='opv' class='val'>0.55</span>
              </label>
            </div>

            <script>
            let map, overlay;
            let pendingOpacity = parseFloat(localStorage.getItem('ndviOpacity') || '0.55');

            function init(){{
              map = new google.maps.Map(document.getElementById('map'), {{
                center:{{lat:52.675,lng:5.80}}, zoom:12, 

              mapTypeId:'hybrid', 
              mapTypeControl:false, // ""Карта/Спутник""
              fullscreenControl: false,  // полноэкранный
              streetViewControl: false,  // ""человечек""
              zoomControl: false,        // +/- зум
              rotateControl: false,      // компас/поворот
              scaleControl: false        // масштаб (внизу)
              }});

              const op = document.getElementById('op');
              const opv = document.getElementById('opv');

              // init UI
              op.value = pendingOpacity.toFixed(2);
              opv.textContent = op.value;

              const apply = (v) => {{
                pendingOpacity = v;
                localStorage.setItem('ndviOpacity', String(v));
                if (overlay) overlay.setOpacity(v);
              }};

              op.addEventListener('input', () => {{
                const v = parseFloat(op.value);
                opv.textContent = v.toFixed(2);
                apply(v);
              }});

              apply(pendingOpacity);
            }}
            init();

            function addOverlayPng(pngUrl, bbox){{
              // поддержка и объекта, и массива [minLon, minLat, maxLon, maxLat]
              const b = Array.isArray(bbox)
                ? {{ minLon: bbox[0], minLat: bbox[1], maxLon: bbox[2], maxLat: bbox[3] }}
                : bbox;

              const imageBounds = {{ south: b.minLat, west: b.minLon, north: b.maxLat, east: b.maxLon }};
              if (overlay) overlay.setMap(null);
              overlay = new google.maps.GroundOverlay(pngUrl, imageBounds, {{ opacity: pendingOpacity }});
              overlay.setMap(map);
              map.fitBounds(new google.maps.LatLngBounds(
                new google.maps.LatLng(imageBounds.south, imageBounds.west),
                new google.maps.LatLng(imageBounds.north, imageBounds.east)
              ));
            }}

            window.chrome?.webview?.addEventListener('message', e => {{
              try {{
                const msg = JSON.parse(e.data);
                if (msg.type === 'overlay') addOverlayPng(msg.url, msg.bbox);
              }} catch(err) {{ console.error(err); }}
            }});
            </script></body></html>";
        }

        // Path to the clips
        private string ClipsDir => Path.Combine(AppContext.BaseDirectory, "data", "clips");

        private HttpListener _listener;

        public struct BBox {

            public BBox(double minLon, double minLat, double maxLon, double maxLat) {
                MinLon = minLon;
                MinLat = minLat;
                MaxLon = maxLon;
                MaxLat = maxLat;

            }
            public double[] ToArray() => new[] { this.MinLon, this.MinLat, this.MaxLon, this.MaxLat };

            public double MinLon;
            public double MinLat;
            public double MaxLon;
            public double MaxLat;
        }


        // GeoJSON bbox в системе координат WGS84(EPSG:4326).
        // Порядок : minLon, minLat, maxLon, maxLat = запад, юг, восток, север.
        // Единицы — десятичные градусы(точка как разделитель), диапазоны: lon[-180..180], lat[-90..90].

        // Пример(Нидерланды, небольшой бокс) :
        // [5.70, 52.60, 5.90, 52.75]


        public BBox box = new BBox(5.70, 52.60, 5.90, 52.75);

        public LocalBloomForm() {
            InitializeComponent();
        }

        // ------------------------------------------------------------
        // UI 
        // ------------------------------------------------------------



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
                    bbox = box.ToArray(),
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


                // после клипа — рисуем первый overlay
                await ShowOverlayForFirstSceneAsync();

                MessageBox.Show(
                    $"Готово.\nКлипов: {tifCount}\nПервая сцена: {firstId}\nHeat points: {heatPts.Count}\nJSON: {heatJsonPath}");
            }
            catch (Exception ex) {
                MessageBox.Show("Ошибка теста клипов:\n" + ex);
            }
        }

        private async void bOpenMap_Click(object sender, EventArgs e) {
            StartLocalHost(5173, GOOGLE_API_KEY);
            await webView21.EnsureCoreWebView2Async();

            webView21.CoreWebView2.NavigationCompleted += async (_, __) => {
                try { await ShowOverlayForFirstSceneAsync(); } catch { /* ignore */ }
            };

            webView21.CoreWebView2.WebMessageReceived += (s, a) =>
                System.Diagnostics.Debug.WriteLine($"[WebView2] {a.WebMessageAsJson}");

            webView21.CoreWebView2.Navigate("http://localhost:5173/");
        }

        private void StartLocalHost(int port, string apiKey) {

           if(_listener!=null) _listener.Stop();

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            string clips = Path.Combine(AppContext.BaseDirectory, "data", "clips");

            _ = Task.Run(async () => {
                while (_listener.IsListening) {
                    var ctx = await _listener.GetContextAsync();
                    var path = ctx.Request.Url!.AbsolutePath.TrimStart('/');

                    if (string.IsNullOrEmpty(path)) {
                        var html = MapHtml(apiKey);
                        var b = Encoding.UTF8.GetBytes(html);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = b.LongLength;
                        await ctx.Response.OutputStream.WriteAsync(b, 0, b.Length);
                        ctx.Response.OutputStream.Close();
                        continue;
                    }

                    var local = Path.Combine(clips, path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(local)) {
                        var bytes = await File.ReadAllBytesAsync(local);
                        ctx.Response.ContentType = GetContentType(local);
                        ctx.Response.ContentLength64 = bytes.LongLength;
                        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        ctx.Response.OutputStream.Close();
                    }
                    else {
                        ctx.Response.StatusCode = 404;
                        await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("404"));
                        ctx.Response.OutputStream.Close();
                    }
                }
            });
        }

        // select the first available id of the type "<ID>_<BAND>.tif"
        private string PickFirstId() {
            var files = Directory.GetFiles(ClipsDir, "*.tif");
            var ids = files
                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                .Select(n => n[..n.LastIndexOf('_')])      // всё до "_B03"
                .Distinct()
                .ToList();
            if (ids.Count == 0) throw new Exception("Нет клипов в data/clips");
            return ids[0];
        }

        // Write heat.json if it doesn't exist yet.
        private string EnsureHeatForId(string id) {
            var outJson = Path.Combine(ClipsDir, $"{id}_heat.json");
            if (!File.Exists(outJson)) {
                var pts = BuildHeat(id); // у тебя эта функция уже есть
                var json = ToHeatmapJson(pts);
                File.WriteAllText(outJson, json);
            }
            return outJson;
        }

        // Sending bbox and heat to WebView2
        private async Task ShowHeatAsync(string apiKey) {
            // HTML in WebView2
            await webView21.EnsureCoreWebView2Async();
            webView21.CoreWebView2.NavigateToString(MapHtml(apiKey));

            // wait for page loading and push data
            webView21.CoreWebView2.NavigationCompleted += (_, __) => {
                // bbox region
                var bboxMsg = System.Text.Json.JsonSerializer.Serialize(new {
                    type = "bbox",
                    box.MinLon,
                    box.MinLat,
                    box.MaxLon,
                    box.MaxLat
                });
                webView21.CoreWebView2.PostWebMessageAsString(bboxMsg);

                // first id and his heat
                var id = PickFirstId();
                var heatPath = EnsureHeatForId(id);
                var pointsJson = File.ReadAllText(heatPath);

                var heatMsg = $"{{\"type\":\"heat\",\"points\":{pointsJson}}}";
                webView21.CoreWebView2.PostWebMessageAsString(heatMsg);
            };
        }

        private short[,] ReadTiff16(string path) {
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

        private List<HeatPoint> BuildHeat(string id) {
            string baseDir = Path.Combine("data", "clips");

            string b03 = Directory.GetFiles(baseDir, $"{id}_B03.tif").FirstOrDefault();
            string b04 = Directory.GetFiles(baseDir, $"{id}_B04.tif").FirstOrDefault();
            string b08 = Directory.GetFiles(baseDir, $"{id}_B08.tif").FirstOrDefault();
            string b05 = Directory.GetFiles(baseDir, $"{id}_B05.tif").FirstOrDefault();
            string fmsk = Directory.GetFiles(baseDir, $"{id}_Fmask.tif").FirstOrDefault();

            if (b03 == null || b04 == null || (b08 == null && b05 == null))
                return new();

            var G = ReadTiff16(b03);
            var R = ReadTiff16(b04);
            var N = ReadTiff16(b08 ?? b05);
            short[,] F = fmsk != null ? ReadTiff16(fmsk) : null;

            int h = G.GetLength(0), w = G.GetLength(1);

            // геометрия клипа → шаги
            double lonStep = (box.MaxLon - box.MinLon) / w;
            double latStep = (box.MaxLat - box.MinLat) / h;


            // ----- бинирование: 32x32 (можно 24/48 по вкусу)
            int bin = 32;
            int bx = Math.Max(1, w / bin);
            int by = Math.Max(1, h / bin);

            var pts = new List<HeatPoint>((w / bx + 1) * (h / by + 1));

            for (int y0 = 0; y0 < h; y0 += by) {
                for (int x0 = 0; x0 < w; x0 += bx) {
                    double sum = 0; int cnt = 0;

                    int y1 = Math.Min(h, y0 + by);
                    int x1 = Math.Min(w, x0 + bx);

                    for (int y = y0; y < y1; y++) {
                        for (int x = x0; x < x1; x++) {
                            if (F != null) {
                                // 4=Cloud, 2=Shadow — пропускаем
                                var c = F[y, x];
                                if (c == 4 || c == 2) continue;
                            }

                            // масштаб 0..1
                            double g = Math.Clamp(G[y, x] / 10000.0, 0, 1);
                            double r = Math.Clamp(R[y, x] / 10000.0, 0, 1);
                            double n = Math.Clamp(N[y, x] / 10000.0, 0, 1);

                            // индексы
                            double gri = (g + r > 1e-6) ? (g - r) / (g + r) : 0; // -1..1
                            double ndvi = (n + r > 1e-6) ? (n - r) / (n + r) : 0; // -1..1

                            // нормализация к 0..1
                            double gri01 = (gri + 1) * 0.5;
                            double ndvi01 = (ndvi + 1) * 0.5;

                            // базовый вес, мягкая компрессия
                            double wgt = Math.Sqrt(Math.Clamp(gri01 * ndvi01, 0, 1));

                            // отрезаем шум
                            if (wgt >= 0.15) {
                                sum += wgt;
                                cnt++;
                            }
                        }
                    }

                    if (cnt > 0) {
                        double avg = sum / cnt;               // средний вес по ячейке
                        double lon = box.MinLon + (x0 + (x1 - x0) * 0.5) * lonStep;
                        double lat = box.MaxLat - (y0 + (y1 - y0) * 0.5) * latStep;

                        pts.Add(new HeatPoint(lat, lon, Math.Clamp(avg, 0, 1)));
                    }
                }
            }

            return pts;
        }

        private string ToHeatmapJson(IEnumerable<HeatPoint> pts) {
            var arr = pts.Select(p => new {
                location = new { lat = p.lat, lng = p.lon },
                weight = Math.Round(p.weight, 3)
            });
            return System.Text.Json.JsonSerializer.Serialize(arr);
        }

        // Ищем, где лежит proj.db (поддерживаем обе раскладки из GISInternals)
        private string ResolveProjDir(string gdalRoot) {
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
        private string ResolveGdalDataDir(string gdalRoot) {
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

        // separable 3x3 [1,2,1]/4, учитывает NaN и не «размывает» в пустоту
        private static float[] Blur3x3NaNAware(float[] src, int w, int h, int passes = 1) {
            var tmp = new float[w * h];
            var dst = new float[w * h];
            Array.Copy(src, dst, src.Length);

            for (int p = 0; p < passes; p++) {
                // горизонталь
                for (int y = 0; y < h; y++) {
                    int row = y * w;
                    for (int x = 0; x < w; x++) {
                        double sum = 0, wsum = 0;

                        void acc(int ix, double wgt) {
                            if (ix < 0 || ix >= w) return;
                            float v = dst[row + ix];
                            if (float.IsFinite(v)) { sum += v * wgt; wsum += wgt; }
                        }

                        acc(x - 1, 1); acc(x, 2); acc(x + 1, 1);
                        tmp[row + x] = wsum > 0 ? (float)(sum / wsum) : float.NaN;
                    }
                }

                // вертикаль
                for (int y = 0; y < h; y++) {
                    for (int x = 0; x < w; x++) {
                        double sum = 0, wsum = 0;

                        void acc(int iy, double wgt) {
                            if (iy < 0 || iy >= h) return;
                            float v = tmp[iy * w + x];
                            if (float.IsFinite(v)) { sum += v * wgt; wsum += wgt; }
                        }

                        acc(y - 1, 1); acc(y, 2); acc(y + 1, 1);
                        src[y * w + x] = wsum > 0 ? (float)(sum / wsum) : float.NaN;
                    }
                }

                Array.Copy(src, dst, src.Length);
            }
            return dst;
        }

        private async Task<string> RunGdalWarpAsync(
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
                    $"-ts {outWidth} 0 -r cubic -multi -wo NUM_THREADS=ALL_CPUS " +
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

        private string ResolveGdalApp(string gdalRoot, string appExe) {
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

        private async Task<string> DownloadWithBearerAsync(string url, string token) {
            string tmp = Path.Combine(Path.GetTempPath(), "hls_" + Guid.NewGuid().ToString("N") + ".tif");
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All });
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = System.IO.File.Create(tmp);
            await resp.Content.CopyToAsync(fs);
            return tmp; // не удаляем сразу – удалим после warp/ошибки
        }

        private async Task ClipFewScenesAsync(IEnumerable<(string id, string url, string band)> assets) {
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

            var tasks = wanted.Select(async a => {
                await sem.WaitAsync();
                try {
                    var fileName = $"{a.id}_{a.band}.tif"
                        .Replace('/', '_').Replace('\\', '_'); // на всякий случай

                    var outTifAbs = Path.Combine(clipsRoot, fileName);  // абсолютный путь

                    if (!System.IO.File.Exists(outTifAbs))
                        await RunGdalWarpAsync(
                            gdalRoot, warpExe,
                            a.url, outTifAbs,           // <-- абсолютный
                             box.MinLon, box.MinLat, box.MaxLon, box.MaxLat,
                            EARTH_LOGIN_TOKEN, 768);
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);


        }

        private async Task ShowOverlayForFirstSceneAsync() {
            await webView21.EnsureCoreWebView2Async();

            var id = PickFirstId();
            var pngPath = await BuildNdviOverlayPngForId(id);

            var msg = JsonSerializer.Serialize(new {
                type = "overlay",
                url = Path.GetFileName(pngPath),  // статика отдаётся из /data/clips
                bbox = box.ToArray()
            });
            webView21.CoreWebView2.PostWebMessageAsString(msg);
        }

        private async Task<string> BuildNdviOverlayPngForId(string id) {
            string clipsDir = Path.Combine(AppContext.BaseDirectory, "data", "clips");

            // вход: B04 (red), NIR (B08 или B05), Fmask (опц.)
            string rPath = Directory.GetFiles(clipsDir, $"{id}_B04.tif").FirstOrDefault()
                ?? throw new Exception("нет клипа B04");
            string nPath = Directory.GetFiles(clipsDir, $"{id}_B08.tif").FirstOrDefault()
                ?? Directory.GetFiles(clipsDir, $"{id}_B05.tif").FirstOrDefault()
                ?? throw new Exception("нет клипа NIR (B08/B05)");
            string fPath = Directory.GetFiles(clipsDir, $"{id}_Fmask.tif").FirstOrDefault();

            // читаем в short[,] (у тебя уже есть)
            var R = ReadTiff16(rPath);
            var N = ReadTiff16(nPath);
            short[,] F = fPath != null ? ReadTiff16(fPath) : null;

            int h = R.GetLength(0), w = R.GetLength(1);
            if (N.GetLength(0) != h || N.GetLength(1) != w) throw new Exception("размеры полос не совпадают");

            // параметры визуалки



            // “инфракрасный” градиент (t∈[0..1]): синий→циан→зелёный→жёлтый→оранж→красный
            var ramp = new[]
            {
                new Stop(0.00,  18,  32, 110,  20),  // deep blue (едва видно)
                new Stop(0.15,  25,  80, 200,  60),  // blue
                new Stop(0.30,  30, 160, 220, 110),  // cyan
                new Stop(0.45,  40, 170,  90, 160),  // green
                new Stop(0.60, 200, 210,  60, 200),  // yellow-ish
                new Stop(0.75, 240, 170,  50, 220),  // orange
                new Stop(0.90, 235,  80,  60, 235),  // red
                new Stop(1.00, 180,  20,  30, 240),  // deep red
            };

            //  считаем NDVI (float) и собираем валид для перцентилей
            var ndvi = new float[h * w];
            int k = 0;
            var vals = new List<float>(h * w / 4); // не храним NaN
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++, k++) {
                    // маска облаков/воды/снега — берём только clear=0. (земля нужна → воду выкидываем)
                    if (F != null) {
                        int c = F[y, x];
                        if (c == 1 || c == 2 || c == 3 || c == 4 || c == 255) { ndvi[k] = float.NaN; continue; }
                    }

                    // масштаб 0..1 из HLS 0..10000
                    double r = Math.Clamp(R[y, x] / 10000.0, 0, 1);
                    double n = Math.Clamp(N[y, x] / 10000.0, 0, 1);

                    double den = (n + r);
                    if (den <= 1e-6) { ndvi[k] = float.NaN; continue; }

                    double v = (n - r) / den;      // -1..1
                    if (v < NDVI_MIN) { ndvi[k] = float.NaN; continue; }

                    ndvi[k] = (float)v;
                    vals.Add((float)v);
                }
            }

            if (vals.Count == 0)
                throw new Exception("после масок/порогов нет валидных NDVI (проверь Fmask/порог)");

            // сгладим карту (убирает «шахматку», уважая NaN)
            var ndviSm = Blur3x3NaNAware(ndvi, w, h, passes: 1);


            // соберём значения для перцентилей уже из сглаженной карты
            vals.Clear();
            for (int i = 0; i < ndviSm.Length; i++)
                if (float.IsFinite(ndviSm[i])) vals.Add(ndviSm[i]);

            // клип по перцентилям и перенос в 0..1
            var (lo, hi) = PercentileClip(CollectionsMarshal.AsSpan(vals), PLO, PHI);
            // safety: на случай, если lo > hi из-за странных данных
            if (hi <= lo) hi = lo + 1e-6;

            // рендер RGBA → PNG (без геопривязки; bounds задашь в GroundOverlay)
            // дальше в рендере используй ndviSm вместо ndvi
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
            unsafe {
                byte* row = (byte*)data.Scan0;
                k = 0;
                for (int y = 0; y < h; y++, row += data.Stride) {
                    byte* px = row;
                    for (int x = 0; x < w; x++, k++) {
                        float v = ndviSm[k];
                        if (!float.IsFinite(v)) {
                            // полностью прозрачный пиксель
                            px[0] = px[1] = px[2] = 0; px[3] = 0; px += 4; continue;
                        }
                        // norm 0..1
                        double t = (v - lo) / (hi - lo);
                        if (t < 0) t = 0; if (t > 1) t = 1;


                        // гамма: красное включается позже
                        t = Math.Pow(t, GAMMA);

                        // интерполяция по ramp
                        Stop s0 = ramp[0], s1 = ramp[^1];
                        for (int i = 1; i < ramp.Length; i++)
                            if (t <= ramp[i].v) { s0 = ramp[i - 1]; s1 = ramp[i]; break; }
                        double dt = (t - s0.v) / Math.Max(1e-9, (s1.v - s0.v));
                        var (r8, g8, b8, a8) = Lerp(s0, s1, dt);

                        // опц. мягкий “перо” по краям, чтобы квадрат не резал глаз
                        int margin = Math.Max(8, Math.Min(w, h) / 64);
                        int dx = Math.Min(x, w - 1 - x), dy = Math.Min(y, h - 1 - y);
                        double edge = Math.Clamp(Math.Min(dx, dy) / (double)margin, 0.0, 1.0);

                        a8 = (byte)Math.Clamp((int)Math.Round(a8 * HEATMAP_ALPHA_SCALE * edge), 0, 255);

                        // BGRA в битмапе Windows
                        px[0] = b8; px[1] = g8; px[2] = r8; px[3] = a8; px += 4;
                    }
                }
            }
            bmp.UnlockBits(data);

            string outPng = Path.Combine(clipsDir, $"{id}_NDVI.png");
            bmp.Save(outPng, ImageFormat.Png);
            return outPng;
        }

        private static string GetContentType(string path) => Path.GetExtension(path).ToLower() switch {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            ".json" => "application/json; charset=utf-8",
            _ => "text/html; charset=utf-8"
        };

        // --------------------------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------------------------

        private struct Stop {
            public double v;
            public byte r, g, b, a;
            public Stop(double v, byte r, byte g, byte b, byte a) {
                this.v = v;
                this.r = r;
                this.g = g;
                this.b = b;
                this.a = a;
            }
        }

        private static (byte r, byte g, byte b, byte a) Lerp(Stop a, Stop b, double t) {
            // t in [0..1]
            byte L(byte A, byte B) => (byte)Math.Clamp(Math.Round(A + (B - A) * t), 0, 255);
            return (L(a.r, b.r), L(a.g, b.g), L(a.b, b.b), L(a.a, b.a));
        }

        private static (double lo, double hi) PercentileClip(ReadOnlySpan<float> vals, double pLo = 5, double pHi = 95) {
            // простая реализация: копия+сортировка; для клипов 256–1024 px это ок
            var buf = vals.ToArray();
            Array.Sort(buf);
            int n = buf.Length;
            if (n == 0) return (0, 1);
            int iLo = (int)Math.Clamp(Math.Round((pLo / 100.0) * (n - 1)), 0, n - 1);
            int iHi = (int)Math.Clamp(Math.Round((pHi / 100.0) * (n - 1)), 0, n - 1);
            return (buf[iLo], buf[iHi] > buf[iLo] ? buf[iHi] : buf[iLo] + 1e-6);
        }
    }
}


