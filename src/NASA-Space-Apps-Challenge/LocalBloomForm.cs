using Microsoft.VisualBasic.ApplicationServices;
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;


namespace NASA_Space_Apps_Challenge {
    public partial class LocalBloomForm : Form {



        private readonly double HEATMAP_ALPHA_SCALE = 1.0;
        private readonly double NDVI_MIN = 0.20; // нижний порог “растительности”
        private readonly double GAMMA = 1.20;        // >1 — красное позже, <1 — раньше
        private readonly double PLO = 5, PHI = 95; // перцентильный клип для стабильного контраста



        // -------------------------------------------------------
        // Progress Bar
        // -------------------------------------------------------

        // UI: progressBar1, labelStatus, btnCancel — добавь на форму в дизайнере
        CancellationTokenSource? _cts;

        // этапы пайплайна (для красивого процентовщика)
        enum Stage { Search = 0, Download = 1, Warp = 2, Bake = 3, Show = 4, Done = 5, Error = 6 }

        record ProgressEvent(Stage stage, int current, int total, string note = "");


        static readonly Dictionary<Stage, double> Weights = new() {
            { Stage.Search,   0.05 },   // 5%  — STAC запрос
            { Stage.Download, 0.20 },   // 20% — качаем TIFF’ы
            { Stage.Warp,     0.45 },   // 45% — gdalwarp (самое долгое)
            { Stage.Bake,     0.25 },   // 25% — NDVI+PNG
            { Stage.Show,     0.05 },   // 5%  — отрисовка оверлея
        };
        static double Acc(Stage s) => Weights.Where(kv => kv.Key < s).Sum(kv => kv.Value);
        static int ToOverallPercent(ProgressEvent ev) {
            double basePart = Acc(ev.stage);
            double frac = ev.total > 0 ? (double)ev.current / ev.total : 0;
            double part = Weights.TryGetValue(ev.stage, out var w) ? w : 0;
            double p = (basePart + frac * part) * 100.0;
            return (int)Math.Clamp(Math.Round(p), 0, 100);
        }



        // -------------------------------------------------------

        private record HeatPoint(double lat, double lon, double weight);

        private string MapHtml(string apiKey) {




            string page = $@"<!doctype html>
                <html>

                <head>
                    <meta charset='utf-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1'>
                    <style>
                        html,
                        body,
                        #map {{
                            height: 100%;
                            margin: 0;
                            padding: 0
                        }}

                        .ctl {{
                            position: absolute;
                            top: 12px;
                            left: 12px;
                            z-index: 5;
                            background: rgba(32, 32, 32, .65);
                            color: #fff;
                            border-radius: 10px;
                            padding: 10px 12px;
                            backdrop-filter: saturate(1.2) blur(2px);
                            font: 14px/1.2 system-ui, Segoe UI, Roboto, Helvetica, Arial
                        }}

                        .ctl input[type=range] {{
                            width: 220px
                        }}

                        .ctl .val {{
                            display: inline-block;
                            min-width: 36px;
                            text-align: right;
                            margin-left: 6px
                        }}
                    </style>
                    <script src='https://maps.googleapis.com/maps/api/js?key={apiKey}&libraries=visualization&language=en'></script>
                </head>
                ";



            page += @"
            <body>


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


                <div id='map'></div>


                <div class='ctl'>
                    <label>Bloom map opacity:
                        <input id='op' type='range' min='0' max='1' step='0.01' value='0.55'>
                        <span id='opv' class='val'>0.55</span>
                    </label>
                </div>


                <script>

            
                    let map, overlay, heatLayer;
                    let pendingOpacity = parseFloat(localStorage.getItem('ndviOpacity') || '0.55');
                    let tileVersion = 1;          // для инвалидации кэша (?v=)
                    let hoverRect = null;       // рамка под курсором
                    let selectedRect = null;    // зафиксированный диапазон (ПКМ)
                    let bakeTimer = null;         // debounce тайл-вычисления
                    let curBboxKey = null;        // текущий набор тайлов
                    let curDateKey = null;


                    function setBakeContext(bboxKey, dateKey){ curBboxKey = bboxKey; curDateKey = dateKey; }


                    // ---------- утилиты тайлов WebMercator ----------
function latLngToTileXY(lat, lng, z) {
  const n = Math.pow(2, z);
  const x = Math.floor((lng + 180.0) / 360.0 * n);
  const y = Math.floor((1.0 - Math.log(Math.tan(lat * Math.PI / 180.0) + 1 / Math.cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * n);
  return { x, y };
}

            function tileBounds(z, x, y) {
  const n = Math.pow(2, z);
  const lon1 = x / n * 360.0 - 180.0;
  const lat1 = Math.atan(Math.sinh(Math.PI * (1 - 2 * y / n))) * 180.0 / Math.PI;
  const lon2 = (x + 1) / n * 360.0 - 180.0;
  const lat2 = Math.atan(Math.sinh(Math.PI * (1 - 2 * (y + 1) / n))) * 180.0 / Math.PI;
  return { west: lon1, south: lat2, east: lon2, north: lat1 }
}


function showTileFrame(z, x, y) {
  const b = tileBounds(z, x, y);
  const opts = {
    strokeColor: '#00FFFF',
    strokeOpacity: 1,
    strokeWeight: 2,
    fillOpacity: 0,
    map,
    bounds: { north: b.north, south: b.south, east: b.east, west: b.west }
  };
  if (!hoverRect) hoverRect = new google.maps.Rectangle(opts);
  else hoverRect.setOptions(opts);
}




            


function hideHoverFrame() {
  if (hoverRect) { hoverRect.setMap(null); hoverRect = null; }
}


// фиксированная рамка (диапазон)
function showSelectedFrame(bounds) {
  // bounds: {west,east,south,north}
  const opts = {
    strokeColor: '#00FFD1',
    strokeOpacity: 1,
    strokeWeight: 2,
    fillOpacity: 0,
    map,
    bounds: { north: bounds.north, south: bounds.south, east: bounds.east, west: bounds.west }
  };
  if (selectedRect) selectedRect.setMap(null);
  selectedRect = new google.maps.Rectangle(opts);
}


function flashSelected() {
  if (!selectedRect) return;
  const prev = selectedRect.get('strokeColor');
  selectedRect.setOptions({ strokeColor: '#FFEA00', strokeWeight: 3 });
  setTimeout(() => selectedRect && selectedRect.setOptions({ strokeColor: prev, strokeWeight: 2 }), 180);
}


                    function postToHost(obj) {
                        try {  window.chrome?.webview?.postMessage(JSON.stringify(obj));  } catch (e) {  console.error(e);  }
                    }


                   // ---------- управление тайловым слоем + опацити ----------
function applyOpacityToTiles() {
  const root = map.getDiv();
  // жёстко фильтруем по origin и префиксу нашего слоя
  const prefix = `${location.origin}/${curBboxKey}/${curDateKey}/z/`;
  const imgs = Array.from(root.querySelectorAll('img'))
    .filter(img => {
      try {
        const u = new URL(img.src, location.href);
        return u.origin === location.origin && u.pathname.startsWith(`/${curBboxKey}/${curDateKey}/z/`);
      } catch { return false; }
    });
  imgs.forEach(img => { img.style.opacity = pendingOpacity.toFixed(2); });
}


                    // helper: объединённые границы по диапазону тайлов
                    function tileRangeBounds(z, x0, x1, y0, y1){
                      const b0 = tileBounds(z, Math.min(x0,x1), Math.min(y0,y1));
                      const b1 = tileBounds(z, Math.max(x0,x1), Math.max(y0,y1));
                      // b1.east/south нужно взять у ""следующего"" тайла по правому/нижнему краю
                      const bE = tileBounds(z, Math.max(x0,x1)+1, Math.min(y0,y1));
                      const bS = tileBounds(z, Math.min(x0,x1), Math.max(y0,y1)+1);
                      return { west: b0.west, east: bE.west, north: b0.north, south: bS.south };
                    }


function addTileLayer(bboxKey, dateKey) {
  curBboxKey = bboxKey;
  curDateKey = dateKey;

  if (overlay) { overlay.setMap(null); overlay = null; }
  if (heatLayer) {
    const idx = map.overlayMapTypes.getArray().indexOf(heatLayer);
    if (idx >= 0) map.overlayMapTypes.removeAt(idx);
    heatLayer = null;
  }

  heatLayer = new google.maps.ImageMapType({
    tileSize: new google.maps.Size(256, 256),
    opacity: pendingOpacity,                      // <— только так
    getTileUrl: (coord, zoom) => {
      const n = 1 << zoom;
      const x = ((coord.x % n) + n) % n;
      const y = coord.y;
      if (y < 0 || y >= n) return null;
      return `/${bboxKey}/${dateKey}/z/${zoom}/${x}/${y}.png?v=${tileVersion}`;
    }
  });

  map.overlayMapTypes.insertAt(0, heatLayer);
}




function resetForeignImgOpacity() {
  const root = map.getDiv();
  const prefix = `/${curBboxKey}/${curDateKey}/z/`;  // наши тайлы
  root.querySelectorAll('img').forEach(img => {
    try {
      const u = new URL(img.src, location.href);
      const isOurs = (u.origin === location.origin) && u.pathname.startsWith(prefix);
      if (!isOurs) img.style.opacity = '';          // <— снять чужое
    } catch {}
  });
}


                    // ---------- (на всякий) режим единого PNG-оверлея ----------
                    function addOverlayPng(pngUrl, bbox) {

                        const b = Array.isArray(bbox) ?
                            { minLon: bbox[0], minLat: bbox[1], maxLon: bbox[2], maxLat: bbox[3] } : bbox;

                        const imageBounds = {
                            south: b.minLat, west: b.minLon, north: b.maxLat, east: b.maxLon
                        };

                        if (overlay) overlay.setMap(null);

                        overlay = new google.maps.GroundOverlay(pngUrl, imageBounds, { opacity: pendingOpacity });
                        overlay.setMap(map);
                        map.fitBounds(new google.maps.LatLngBounds(
                            new google.maps.LatLng(imageBounds.south, imageBounds.west),
                            new google.maps.LatLng(imageBounds.north, imageBounds.east)
                        ));
                    }


                    function init() {

                        map = new google.maps.Map(
                            document.getElementById('map'),
                            {
                                center: { lat: 52.675, lng: 5.80 },
                                zoom: 12,
                                mapTypeId: 'hybrid',
                                mapTypeControl: false, fullscreenControl: false, streetViewControl: false,
                                zoomControl: false, rotateControl: false, scaleControl: false
                            }
                        );

 map.getDiv().addEventListener('contextmenu', e => e.preventDefault());

                        const op = document.getElementById('op');
                        const opv = document.getElementById('opv');


// Слайдер — меняем opacity только на heatLayer и чистим чужие остатки
const apply = (v) => {
  pendingOpacity = v;
  localStorage.setItem('ndviOpacity', String(v));
  if (heatLayer && typeof heatLayer.setOpacity === 'function') {
    heatLayer.setOpacity(v);
  }
  if (overlay) overlay.setOpacity(v);
  resetForeignImgOpacity();                        // <— важный вызов
};




                        op.value = pendingOpacity.toFixed(2);
                        opv.textContent = op.value;

                        op.addEventListener('input', () => {
                            const v = parseFloat(op.value);
                            opv.textContent = v.toFixed(2);
                            apply(v);
                        });

                        apply(pendingOpacity);

map.addListener('mousemove', (ev) => {
  const z = map.getZoom();
  if (z == null) return;

  const { x, y } = latLngToTileXY(ev.latLng.lat(), ev.latLng.lng(), z);
  showTileFrame(z, x, y);

  if (curBboxKey && curDateKey) {
    clearTimeout(bakeTimer);
    bakeTimer = setTimeout(() => {
      postToHost({ type:'bakeTile', bboxKey:curBboxKey, dateKey:curDateKey, z, x, y });
    }, 250);
  }
});

map.addListener('zoom_changed', () => hideHoverFrame());
map.addListener('dragstart', () => hideHoverFrame());

map.addListener('rightclick', (ev) => {
  const z = map.getZoom();
  if (z == null) return;

  const { x, y } = latLngToTileXY(ev.latLng.lat(), ev.latLng.lng(), z);

  const R = (ev.domEvent || {}).ctrlKey ? 2 : ((ev.domEvent || {}).shiftKey ? 1 : 0);
  const x0 = x - R, x1 = x + R, y0 = y - R, y1 = y + R;

  const rb = tileRangeBounds(z, x0, x1, y0, y1);
  showSelectedFrame(rb);

  if (curBboxKey && curDateKey) {
    postToHost({ type:'bakeRegion', bboxKey:curBboxKey, dateKey:curDateKey, z, x0, x1, y0, y1 });
  }
});

                    }


init();

if (!window.__webviewHandlerBound) {
  window.__webviewHandlerBound = true;
  window.chrome?.webview?.addEventListener('message', (e) => {
    try {
      const msg = JSON.parse(e.data);
      switch (msg.type) {
        case 'overlay':
          addOverlayPng(msg.url, msg.bbox);
          break;
        case 'tileLayer':
          addTileLayer(msg.bboxKey, msg.dateKey);
          break;
        case 'bakeContext':
          setBakeContext(msg.bboxKey, msg.dateKey);
          break;
        case 'tileReady': {
          // инвалидация тайлов
          tileVersion++;
          const arr = map.overlayMapTypes.getArray();
          const idx = arr.indexOf(heatLayer);
          if (idx >= 0) {
            map.overlayMapTypes.removeAt(idx);
            map.overlayMapTypes.insertAt(idx, heatLayer);
          }
          // мягко мигнём именно фиксированной рамкой (если есть)
          flashSelected();
          break;
        }
        default:
          console.warn('Unknown message from host:', msg);
      }
    } catch (err) {
      console.error('WebView message parse/handle error:', err, e.data);
    }
  });



} 



                </script>
            </body>

            </html>";


            return page;
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

        public BBox box = new BBox(5.70, 52.60, 5.90, 52.75);

        public LocalBloomForm() {

            InitializeComponent();

            pbStatus.Minimum = 0;
            pbStatus.Maximum = 100;
            pbStatus.Value = 0;
            pbStatus.Text = "idle";


            this.FormClosing += (s, e) => { if (_listener != null) _listener.Stop(); };
            this.Shown += (s, e) => { OpenMap(); };


            this.timelineControl1.DateChanged += timelineControl1_DateChanged;
        }



    //    private async Task BakeDayFixedGridAsync(
    //List<(string id, string url, string band)> hrefs,
    //DateTime utcDay, BBox b,
    //IProgress<ProgressEvent> progress,
    //CancellationToken ct) {
    //        // выбери сцену + посчитай общий NDVI буфер как у тебя
    //        var id = hrefs.Select(h => h.id).Distinct()
    //                      .OrderByDescending(s => s.Contains("HLSS30", StringComparison.OrdinalIgnoreCase))
    //                      .ThenBy(s => s)
    //                      .First();

    //        var (ndviSm, w, h, clip, ramp) = BuildNdviBufferForId(id, out var geoBounds);

    //        var tilesRoot = TilesDir(utcDay, b);
    //        Directory.CreateDirectory(tilesRoot);

    //        var (xMin, xMax, yMin, yMax) = TilesCoveringBBox(Z_FIXED, b);
    //        int total = (xMax - xMin + 1) * (yMax - yMin + 1), done = 0;

    //        for (int x = xMin; x <= xMax; x++)
    //            for (int y = yMin; y <= yMax; y++) {
    //                ct.ThrowIfCancellationRequested();
    //                string outAbs = TilePath(tilesRoot, x, y);
    //                if (!File.Exists(outAbs))
    //                    RenderTileFromNdvi(ndviSm, w, h, geoBounds, clip, ramp, GAMMA, outAbs, Z_FIXED, x, y);
    //                if ((++done % 20) == 0)
    //                    progress.Report(new(Stage.Bake, done, total, $"grid {done}/{total}"));
    //            }
    //    }




        private async Task<(List<(string id, string url, string band)> hrefs, DateTime day)>
SearchForDayAsync(DateTime utcDay, CancellationToken ct) {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Keys.EARTH_LOGIN_TOKEN);

            var start = new DateTime(utcDay.Year, utcDay.Month, utcDay.Day, 0, 0, 0, DateTimeKind.Utc);
            var endExcl = new DateTime(utcDay.Year, utcDay.Month, utcDay.Day, 23, 59, 0, DateTimeKind.Utc);

            var body = new {
                collections = new[] { "HLSS30.v2.0", "HLSL30.v2.0" },
                bbox = box.ToArray(),
                datetime = $"{start:yyyy-MM-ddTHH:mm:ssZ}/{endExcl:yyyy-MM-ddTHH:mm:ssZ}",
                limit = 200
            };
            var req = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync("https://cmr.earthdata.nasa.gov/stac/LPCLOUD/search", req, ct);
            var respText = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();

            var hrefs = new List<(string id, string url, string band)>();
            using var doc = JsonDocument.Parse(respText);
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray()) {
                var id = f.GetProperty("id").GetString()!;
                var assets = f.GetProperty("assets");
                foreach (var band in new[] { "B03", "B04", "B08", "B05", "Fmask" })
                    if (assets.TryGetProperty(band, out var a) && a.TryGetProperty("href", out var href))
                        hrefs.Add((id, href.GetString()!, band));
            }
            return (hrefs, start);
        }


        private async Task PostBakeContextAsync(DateTime utcDay, BBox b) {
            await webView21.EnsureCoreWebView2Async();
            var msg = JsonSerializer.Serialize(new {
                type = "bakeContext",
                bboxKey = BboxKey(b),
                dateKey = DateKey(utcDay)
            });
            webView21.CoreWebView2.PostWebMessageAsString(msg);
        }


        private async void timelineControl1_DateChanged(object? sender, EventArgs e) {
            var d = timelineControl1.Current;
            var dayUtc = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

            await PostBakeContextAsync(dayUtc, box);
            await PostTileLayerAsync(dayUtc, box);

            if (!await TryShowCachedTilesAsync(dayUtc, box)) {
                lStatus.Text = "no cache for this day";
                pbStatus.Value = 0;
            }
        }


        private async void button1_Click(object sender, EventArgs e) {
            button1.Enabled = false;
            //btnCancel.Enabled = true;
            pbStatus.Value = 0;
            lStatus.Text = "starting…";

            _cts = new CancellationTokenSource();

            var ui = new Progress<ProgressEvent>(ev => {
                pbStatus.Value = ToOverallPercent(ev);
                // короткий статус: Stage N/N — note
                lStatus.Text = $"{ev.stage} {ev.current}/{ev.total}  {ev.note}";
            });

            try {
                await RunPipelineAsync(ui, _cts.Token);
                lStatus.Text = "done";
                pbStatus.Value = 100;
            }
            catch (OperationCanceledException) {
                lStatus.Text = "canceled";
            }
            catch (Exception ex) {
                lStatus.Text = "error: " + ex.Message;
            }
            finally {
                // btnCancel.Enabled = false;
                button1.Enabled = true;
                _cts?.Dispose(); _cts = null;
            }
        }


        private async Task RunPipelineAsync(IProgress<ProgressEvent> progress, CancellationToken ct) {
            // --- Search
            progress.Report(new(Stage.Search, 0, 1, "STAC"));
            var (hrefs, dayStartUtc) = await SearchAsync(ct);
            progress.Report(new(Stage.Search, 1, 1, $"found {hrefs.Count} assets @ {dayStartUtc:yyyy-MM-dd}"));

            // если ничего не нашли — красиво завершаем
            if (hrefs.Count == 0) { progress.Report(new(Stage.Done, 1, 1, "no assets")); return; }



            if (await TryShowCachedTilesAsync(dayStartUtc, box)) {
                progress.Report(new(Stage.Show, 1, 1, "cached tiles"));
                progress.Report(new(Stage.Done, 1, 1, "done"));
                return;
            }

            ct.ThrowIfCancellationRequested();

            // --- Download + Warp (как у тебя сейчас, с прогрессом)
            var wanted = hrefs
                .GroupBy(a => a.id).Take(4)
                .SelectMany(g => g.Where(a => a.band is "B03" or "B04" or "B08" or "B05" or "Fmask"))
                .ToList();

            int total = wanted.Count;
            int dlDone = 0, wpDone = 0;

            await ClipFewScenesWithProgressAsync(
                wanted,
                onDownload: (id, band, done) => {
                    Interlocked.Exchange(ref dlDone, done);
                    progress.Report(new(Stage.Download, dlDone, total, $"{id}/{band}"));
                },
                onWarp: (id, band, done) => {
                    Interlocked.Exchange(ref wpDone, done);
                    progress.Report(new(Stage.Warp, wpDone, total, $"{id}/{band}"));
                },
                ct: ct);

            ct.ThrowIfCancellationRequested();

            //await PostBakeContextAsync(dayStartUtc, box);   // контекст для наведения/ПКМ
            //await PostTileLayerAsync(dayStartUtc, box);     // включить слой даже если тайлов нет

            //progress.Report(new(Stage.Done, 1, 1, "done"));


            // ----- ВЫПЕЧКА ТАЙЛОВ ДЛЯ ВСЕГО ОКНА -----
            progress.Report(new(Stage.Bake, 0, 1, "tiling NDVI…"));
            await BakeDayTilesAsync(hrefs, dayStartUtc, box, progress, ct);

            // фронту достаточно tileLayer из BakeDayTilesAsync, но обновим контекст на всякий
            await PostBakeContextAsync(dayStartUtc, box);
            progress.Report(new(Stage.Done, 1, 1, "done"));


        }


        private async Task PostTileLayerAsync(DateTime utcDay, BBox b) {
            await webView21.EnsureCoreWebView2Async();
            var msg = JsonSerializer.Serialize(new {
                type = "tileLayer",
                bboxKey = BboxKey(b),
                dateKey = DateKey(utcDay)   // yyyyMMdd
            });
            webView21.CoreWebView2.PostWebMessageAsString(msg);
        }

        private async Task<(List<(string id, string url, string band)> hrefs, DateTime day)>
    SearchAsync(CancellationToken ct) {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Keys.EARTH_LOGIN_TOKEN);

            var selDate = this.timelineControl1.Current;
            var start = new DateTime(selDate.Year, selDate.Month, selDate.Day, 0, 0, 0, DateTimeKind.Utc);
            var endExcl = new DateTime(selDate.Year, selDate.Month, selDate.Day, 23, 59, 0, DateTimeKind.Utc);

            var body = new {
                collections = new[] { "HLSS30.v2.0", "HLSL30.v2.0" },
                bbox = box.ToArray(),
                datetime = $"{start:yyyy-MM-ddTHH:mm:ssZ}/{endExcl:yyyy-MM-ddTHH:mm:ssZ}",
                limit = 200
            };
            var req = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync("https://cmr.earthdata.nasa.gov/stac/LPCLOUD/search", req, ct);
            var respText = await resp.Content.ReadAsStringAsync(ct);
            resp.EnsureSuccessStatusCode();

            var hrefs = new List<(string id, string url, string band)>();
            using var doc = JsonDocument.Parse(respText);
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray()) {
                var id = f.GetProperty("id").GetString()!;
                var assets = f.GetProperty("assets");
                foreach (var band in new[] { "B03", "B04", "B08", "B05", "Fmask" })
                    if (assets.TryGetProperty(band, out var a) && a.TryGetProperty("href", out var href))
                        hrefs.Add((id, href.GetString()!, band));
            }
            return (hrefs, start);
        }


        private async Task ClipFewScenesWithProgressAsync(
    List<(string id, string url, string band)> assets,
    Action<string, string, int> onDownload,
    Action<string, string, int> onWarp,
    CancellationToken ct) {
            Directory.CreateDirectory(ClipsDir);
            var gdalRoot = Path.Combine(AppContext.BaseDirectory, "release-1930-x64-gdal-3-11-3-mapserver-8-4-0");
            var warpExe = ResolveGdalApp(gdalRoot, "gdalwarp.exe");

            var sem = new SemaphoreSlim(3);
            int idx = 0;

            var tasks = assets.Select(async a => {
                await sem.WaitAsync(ct);
                try {
                    ct.ThrowIfCancellationRequested();

                    var fileName = $"{a.id}_{a.band}.tif".Replace('/', '_').Replace('\\', '_');
                    var outTifAbs = Path.Combine(ClipsDir, fileName);

                    // download (отчёт “поштучно”, без байтов — достаточно честно)
                    onDownload(a.id, a.band, Interlocked.Increment(ref idx));

                    if (!File.Exists(outTifAbs)) {
                        // warp — самое долгое; регистрируем отмену → Kill()
                        await RunGdalWarpAsync_Cancellable(
                            gdalRoot, warpExe,
                            a.url, outTifAbs,
                            box.MinLon, box.MinLat, box.MaxLon, box.MaxLat,
                            Keys.EARTH_LOGIN_TOKEN, 768, ct);
                    }

                    onWarp(a.id, a.band, idx);
                }
                finally { sem.Release(); }
            });

            await Task.WhenAll(tasks);
        }


        private async Task<string> RunGdalWarpAsync_Cancellable(
    string gdalRoot, string exePath, string url, string outTif,
    double minLon, double minLat, double maxLon, double maxLat,
    string earthToken, int outWidth, CancellationToken ct) {
            string binDir = Path.Combine(gdalRoot, "bin");
            string gdalData = ResolveGdalDataDir(gdalRoot);
            string projDir = ResolveProjDir(gdalRoot);

            string srcLocal = await DownloadWithBearerAsync(url, earthToken);  // можно тоже сделать отменяемым

            try {
                var args =
                  $"--config GDAL_DATA \"{gdalData}\" --config PROJ_LIB \"{projDir}\" " +
                  $"--config GDAL_DISABLE_READDIR_ON_OPEN YES --config VSI_CACHE TRUE " +
                  $"-t_srs EPSG:4326 -te_srs EPSG:4326 -te {minLon} {minLat} {maxLon} {maxLat} " +
                  $"-ts {outWidth} 0 -r cubic -multi -wo NUM_THREADS=ALL_CPUS -of GTiff " +
                  $@"""{srcLocal}"" ""{outTif}""";

                var psi = new System.Diagnostics.ProcessStartInfo(exePath, args) {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = binDir
                };
                psi.Environment["PATH"] = binDir + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
                psi.Environment["PROJ_LIB"] = projDir;
                psi.Environment["GDAL_DATA"] = gdalData;

                using var p = System.Diagnostics.Process.Start(psi)!;
                using var _ = ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } });

                var stdTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();

                await Task.Run(() => p.WaitForExit(), ct); // уважает токен (через Kill)
                if (p.ExitCode != 0) {
                    var err = await errTask;
                    throw new Exception(err);
                }
                return outTif;
            }
            finally {
                try { System.IO.File.Delete(srcLocal); } catch { }
            }
        }


        private async void OpenMap() {

            StartLocalHost(5173, Keys.GOOGLE_API_KEY);

            await webView21.EnsureCoreWebView2Async();
            webView21.CoreWebView2.NavigationCompleted += async (_, __) => {
                var todayUtc = DateTime.UtcNow.Date;
                await PostBakeContextAsync(todayUtc, box);
                await PostTileLayerAsync(todayUtc, box);   // не ждём кэша

                // если тайлы уже есть — подключим слой (опционально)
                if (!await TryShowCachedTilesAsync(todayUtc, box))
                    lStatus.Text = "no cached tiles for today";
            };


            webView21.CoreWebView2.WebMessageReceived += async (s, a) => {
                try {
                    using var doc = JsonDocument.Parse(a.WebMessageAsJson);
                    if (!doc.RootElement.TryGetProperty("type", out var tp)) return;
                    var t = tp.GetString();
                    if (t == "bakeTile") {
                        var dateKey = doc.RootElement.GetProperty("dateKey").GetString()!;
                        var z = doc.RootElement.GetProperty("z").GetInt32();
                        var x = doc.RootElement.GetProperty("x").GetInt32();
                        var y = doc.RootElement.GetProperty("y").GetInt32();
                        var day = DateTime.ParseExact(dateKey, "yyyyMMdd", CultureInfo.InvariantCulture,
                                                      DateTimeStyles.AssumeUniversal).Date;

                        await BakeSingleTileAsync(day, box, z, x, y);
                        webView21.CoreWebView2.PostWebMessageAsString(
                            JsonSerializer.Serialize(new { type = "tileReady", z, x, y }));
                    }
                    else if (t == "bakeRegion") {
                        var dateKey = doc.RootElement.GetProperty("dateKey").GetString()!;
                        var z = doc.RootElement.GetProperty("z").GetInt32();
                        int x0 = doc.RootElement.GetProperty("x0").GetInt32();
                        int x1 = doc.RootElement.GetProperty("x1").GetInt32();
                        int y0 = doc.RootElement.GetProperty("y0").GetInt32();
                        int y1 = doc.RootElement.GetProperty("y1").GetInt32();
                        var day = DateTime.ParseExact(dateKey, "yyyyMMdd", CultureInfo.InvariantCulture,
                                                      DateTimeStyles.AssumeUniversal).Date;

                        await BakeRegionAsync(day, box, z, x0, x1, y0, y1);
                    }
                }
                catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine("[WebView2] message error: " + ex);
                }
            };


            webView21.CoreWebView2.Navigate("http://localhost:5173/");

        }


        private async Task BakeRegionAsync(DateTime utcDay, BBox viewBbox, int z, int x0, int x1, int y0, int y1) {
            var (test, _) = await SearchForDayAsync(utcDay, CancellationToken.None);
            if (test.Count == 0) return;

            int n = 1 << z;
            int _wrapX(int x) => ((x % n) + n) % n;

            int yMin = Math.Max(0, Math.Min(y0, y1));
            int yMax = Math.Min(n - 1, Math.Max(y0, y1));

            for (int xi = Math.Min(x0, x1); xi <= Math.Max(x0, x1); xi++) {
                int X = _wrapX(xi);
                for (int Y = yMin; Y <= yMax; Y++) {
                    await BakeSingleTileAsync(utcDay, viewBbox, z, X, Y);
                    webView21.CoreWebView2.PostWebMessageAsString(
                        JsonSerializer.Serialize(new { type = "tileReady", z, x = X, y = Y }));
                }
            }
        }

        private async Task BakeSingleTileAsync(DateTime utcDay, BBox viewBbox, int z, int x, int y) {
            var (hrefs, _) = await SearchForDayAsync(utcDay, CancellationToken.None);
            if (hrefs.Count == 0) return;

            var sceneId = hrefs.Select(h => h.id).Distinct()
                               .OrderByDescending(s => s.Contains("HLSS30", StringComparison.OrdinalIgnoreCase))
                               .ThenBy(s => s)
                               .First();

            var tb = TileBounds(z, x, y);
            var tileBbox = new BBox(tb.minLon, tb.minLat, tb.maxLon, tb.maxLat);

            var needed = hrefs.Where(h => h.id == sceneId && (h.band is "B04" or "B08" or "B05" or "Fmask")).ToList();
            await ClipSpecificBBoxAsync(needed, tileBbox);

            var (ndviSm, w, h, clip, ramp) = BuildNdviBufferForId_UsingBBox(sceneId, tileBbox);

            var outAbs = TilePath(TilesDir(utcDay, viewBbox), z, x, y);
            RenderTileFromNdvi(ndviSm, w, h, tileBbox, clip, ramp, GAMMA, outAbs, z, x, y);
        }




        //private async Task BakeSingleTileAsync(DateTime utcDay, BBox viewBbox, int x, int y) {
        //    var (hrefs, _) = await SearchForDayAsync(utcDay, CancellationToken.None);
        //    if (hrefs.Count == 0) return;

        //    var sceneId = hrefs.Select(h => h.id).Distinct()
        //                       .OrderByDescending(s => s.Contains("HLSS30", StringComparison.OrdinalIgnoreCase))
        //                       .ThenBy(s => s)
        //                       .First();

        //    // тайловые границы ТОЛЬКО уровня Z_FIXED
        //    var tb = TileBounds(Z_FIXED, x, y);
        //    var tileBbox = new BBox(tb.minLon, tb.minLat, tb.maxLon, tb.maxLat);

        //    var needed = hrefs.Where(h => h.id == sceneId && (h.band is "B04" or "B08" or "B05" or "Fmask")).ToList();
        //    await ClipSpecificBBoxAsync(needed, tileBbox);

        //    var (ndviSm, w, h, clip, ramp) = BuildNdviBufferForId_UsingBBox(sceneId, tileBbox);

        //    var outAbs = TilePath(TilesDir(utcDay, viewBbox), x, y);
        //    RenderTileFromNdvi(ndviSm, w, h, tileBbox, clip, ramp, GAMMA, outAbs, Z_FIXED, x, y);
        //}


        private async Task ClipSpecificBBoxAsync(
    List<(string id, string url, string band)> assets, BBox b) {
            Directory.CreateDirectory(ClipsDir);
            var gdalRoot = Path.Combine(AppContext.BaseDirectory, "release-1930-x64-gdal-3-11-3-mapserver-8-4-0");
            var warpExe = ResolveGdalApp(gdalRoot, "gdalwarp.exe");

            foreach (var a in assets) {
                var fileName = $"{a.id}_{a.band}__{BboxKey(b)}.tif".Replace('/', '_').Replace('\\', '_');
                var outTifAbs = Path.Combine(ClipsDir, fileName);
                if (!File.Exists(outTifAbs)) {
                    await RunGdalWarpAsync(gdalRoot, warpExe, a.url, outTifAbs,
                        b.MinLon, b.MinLat, b.MaxLon, b.MaxLat, Keys.EARTH_LOGIN_TOKEN, 768);
                }
            }
        }


        private (float[] ndviSm, int w, int h, (double lo, double hi) clip, Stop[] ramp)
    BuildNdviBufferForId_UsingBBox(string id, BBox b) {
            string clipsDir = ClipsDir;
            // ищем файлы именно с ключом этого bbox: *_<BAND>__<bboxKey>.tif
            string key = BboxKey(b);

            string rPath = Directory.GetFiles(clipsDir, $"{id}_B04__{key}.tif").FirstOrDefault()
                ?? throw new Exception("нет клипа B04 для тайла");
            string nPath = Directory.GetFiles(clipsDir, $"{id}_B08__{key}.tif").FirstOrDefault()
                ?? Directory.GetFiles(clipsDir, $"{id}_B05__{key}.tif").FirstOrDefault()
                ?? throw new Exception("нет клипа NIR (B08/B05) для тайла");
            string fPath = Directory.GetFiles(clipsDir, $"{id}_Fmask__{key}.tif").FirstOrDefault();

            var R = ReadTiff16(rPath);
            var N = ReadTiff16(nPath);
            short[,] F = fPath != null ? ReadTiff16(fPath) : null;

            int h = R.GetLength(0), w = R.GetLength(1);
            if (N.GetLength(0) != h || N.GetLength(1) != w) throw new Exception("размеры полос не совпадают");

            var ndvi = new float[w * h];
            var vals = new List<float>(w * h / 4);
            int k = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++, k++) {
                    if (F != null) {
                        int c = F[y, x];
                        if (c == 1 || c == 2 || c == 3 || c == 4 || c == 255) { ndvi[k] = float.NaN; continue; }
                    }
                    double r = Math.Clamp(R[y, x] / 10000.0, 0, 1);
                    double n = Math.Clamp(N[y, x] / 10000.0, 0, 1);
                    double den = (n + r);
                    if (den <= 1e-6) { ndvi[k] = float.NaN; continue; }
                    double v = (n - r) / den;
                    if (v < NDVI_MIN) { ndvi[k] = float.NaN; continue; }
                    ndvi[k] = (float)v;
                    vals.Add((float)v);
                }

            var ndviSm = Blur3x3NaNAware(ndvi, w, h, passes: 1);

            vals.Clear();
            for (int i = 0; i < ndviSm.Length; i++) if (float.IsFinite(ndviSm[i])) vals.Add(ndviSm[i]);
            var clip = PercentileClip(CollectionsMarshal.AsSpan(vals), PLO, PHI);

            var ramp = new[] {
        new Stop(0.00,18,32,110,20), new Stop(0.15,25,80,200,60),
        new Stop(0.30,30,160,220,110), new Stop(0.45,40,170,90,160),
        new Stop(0.60,200,210,60,200), new Stop(0.75,240,170,50,220),
        new Stop(0.90,235,80,60,235), new Stop(1.00,180,20,30,240),
    };

            return (ndviSm, w, h, clip, ramp);
        }
        private void StartLocalHost(int port, string apiKey) {
            if (_listener != null) _listener.Stop();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            string clips = ClipsDir;
            string bakes = BakesRoot;

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

                    // пробуем из bakes, затем из clips
                    string local = Path.Combine(bakes, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(local))
                        local = Path.Combine(clips, path.Replace('/', Path.DirectorySeparatorChar));

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
                            Keys.EARTH_LOGIN_TOKEN, 768);
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);


        }



        private async Task<(string relUrl, string absPath)> BuildNdviOverlayPngForId(
    string id,
    string outDirAbs,   // data/bakes/<bboxKey>/<yyyyMMdd>
    Action<int, int, string>? report = null,
    CancellationToken ct = default) {
            Directory.CreateDirectory(outDirAbs);


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

            var ndviSm = Blur3x3NaNAware(ndvi, w, h, passes: 1);


            vals.Clear();
            for (int i = 0; i < ndviSm.Length; i++)
                if (float.IsFinite(ndviSm[i])) vals.Add(ndviSm[i]);

            var (lo, hi) = PercentileClip(CollectionsMarshal.AsSpan(vals), PLO, PHI);
            // safety: на случай, если lo > hi из-за странных данных
            if (hi <= lo) hi = lo + 1e-6;

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

            string fileName = $"{id}_NDVI.png";
            string outPngAbs = Path.Combine(outDirAbs, fileName);
            bmp.Save(outPngAbs, ImageFormat.Png);



            var rel = Path.GetRelativePath(BakesRoot, outPngAbs)
                         .Replace(Path.DirectorySeparatorChar, '/');

            report?.Invoke(100, 100, "png saved");
            return ($"{rel}", outPngAbs);
        }



        private (float[] ndviSm, int w, int h, (double lo, double hi) clip, Stop[] ramp)
    BuildNdviBufferForId(string id, out BBox geoBounds) {
            string clipsDir = Path.Combine(AppContext.BaseDirectory, "data", "clips");

            string rPath = Directory.GetFiles(clipsDir, $"{id}_B04.tif").FirstOrDefault()
                ?? throw new Exception("нет клипа B04");
            string nPath = Directory.GetFiles(clipsDir, $"{id}_B08.tif").FirstOrDefault()
                ?? Directory.GetFiles(clipsDir, $"{id}_B05.tif").FirstOrDefault()
                ?? throw new Exception("нет клипа NIR (B08/B05)");
            string fPath = Directory.GetFiles(clipsDir, $"{id}_Fmask.tif").FirstOrDefault();

            var R = ReadTiff16(rPath);
            var N = ReadTiff16(nPath);
            short[,] F = fPath != null ? ReadTiff16(fPath) : null;

            int h = R.GetLength(0), w = R.GetLength(1);
            if (N.GetLength(0) != h || N.GetLength(1) != w) throw new Exception("размеры полос не совпадают");

            // границы растра = текущий bbox (важно для геопривязки выборки)
            geoBounds = box;

            // считаем NDVI
            var ndvi = new float[w * h];
            var vals = new List<float>(w * h / 4);
            int k = 0;
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++, k++) {
                    if (F != null) {
                        int c = F[y, x];
                        if (c == 1 || c == 2 || c == 3 || c == 4 || c == 255) { ndvi[k] = float.NaN; continue; }
                    }
                    double r = Math.Clamp(R[y, x] / 10000.0, 0, 1);
                    double n = Math.Clamp(N[y, x] / 10000.0, 0, 1);
                    double den = (n + r);
                    if (den <= 1e-6) { ndvi[k] = float.NaN; continue; }
                    double v = (n - r) / den;
                    if (v < NDVI_MIN) { ndvi[k] = float.NaN; continue; }

                    ndvi[k] = (float)v;
                    vals.Add((float)v);
                }
            }
            if (vals.Count == 0) throw new Exception("после масок/порогов нет валидных NDVI");

            var ndviSm = Blur3x3NaNAware(ndvi, w, h, passes: 1);

            // перцентили по сглаженной карте
            vals.Clear();
            for (int i = 0; i < ndviSm.Length; i++)
                if (float.IsFinite(ndviSm[i])) vals.Add(ndviSm[i]);
            var clip = PercentileClip(CollectionsMarshal.AsSpan(vals), PLO, PHI);

            // та же палитра
            var ramp = new[]
            {
        new Stop(0.00,  18,  32, 110,  20),
        new Stop(0.15,  25,  80, 200,  60),
        new Stop(0.30,  30, 160, 220, 110),
        new Stop(0.45,  40, 170,  90, 160),
        new Stop(0.60, 200, 210,  60, 200),
        new Stop(0.75, 240, 170,  50, 220),
        new Stop(0.90, 235,  80,  60, 235),
        new Stop(1.00, 180,  20,  30, 240),
    };

            return (ndviSm, w, h, clip, ramp);
        }

        private async Task BakeDayTilesAsync(
    List<(string id, string url, string band)> hrefs,
    DateTime utcDay, BBox b,
    IProgress<ProgressEvent> progress,
    CancellationToken ct) {
            string dayDir = BakeDir(utcDay, b);
            Directory.CreateDirectory(dayDir);
            string tilesRoot = TilesDir(utcDay, b);


            var id = hrefs.Select(h => h.id).Distinct()
                          .OrderByDescending(s => s.Contains("HLSS30", StringComparison.OrdinalIgnoreCase))
                          .ThenBy(s => s)
                          .First();

            progress.Report(new(Stage.Bake, 0, 1, $"tiling {id}"));


            var (ndviSm, w, h, clip, ramp) = BuildNdviBufferForId(id, out var geoBounds);


            var zooms = new List<ZoomRange>();
            for (int z = Z_MIN; z <= Z_MAX; z++) {
                var (xMin, xMax, yMin, yMax) = TilesCoveringBBox(z, b);
                zooms.Add(new ZoomRange(z, xMin, xMax, yMin, yMax));

                int total = (xMax - xMin + 1) * (yMax - yMin + 1);
                int done = 0;

                for (int x = xMin; x <= xMax; x++) {
                    for (int y = yMin; y <= yMax; y++) {
                        ct.ThrowIfCancellationRequested();
                        string outAbs = TilePath(tilesRoot, z, x, y);
                        if (!File.Exists(outAbs)) {
                            RenderTileFromNdvi(ndviSm, w, h, geoBounds, clip, ramp, GAMMA, outAbs, z, x, y);
                        }
                        done++;
                        if ((done % 20) == 0)
                            progress.Report(new(Stage.Bake, done, total, $"z{z} {done}/{total}"));
                    }
                }
                progress.Report(new(Stage.Bake, 1, 1, $"z{z} done"));
            }


            WriteTileManifest(dayDir, utcDay, b, zooms);


            await PostTileLayerAsync(utcDay, b);
        }



        private void WriteBakeManifest(string dirAbs, string bboxKey, DateTime utc, IEnumerable<string> relFiles) {
            var files = relFiles.ToArray();
            var preferred = files.FirstOrDefault(); // стратегию можно улучшить (например, Sentinel > Landsat)
            var man = new BakeManifest(utc.ToString("yyyy-MM-dd"), bboxKey, files, preferred);

            File.WriteAllText(Path.Combine(dirAbs, "manifest.json"),
                System.Text.Json.JsonSerializer.Serialize(man, new JsonSerializerOptions { WriteIndented = true }));
        }


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

        private static string GetContentType(string path) => Path.GetExtension(path).ToLower() switch {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            ".json" => "application/json; charset=utf-8",
            _ => "text/html; charset=utf-8"
        };

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


        private async Task PostOverlayAsync(string relUrl, double[] bbox) {
            await webView21.EnsureCoreWebView2Async();
            var msg = JsonSerializer.Serialize(new {
                type = "overlay",
                url = relUrl,     // ВАЖНО: относительный путь относительно BakesRoot (см. StartLocalHost)
                bbox = bbox
            });
            webView21.CoreWebView2.PostWebMessageAsString(msg);
        }


        // Попытка мгновенно показать слой из кэша (возвращает true, если получилось)
        private async Task<bool> TryShowCachedOverlayAsync(DateTime utcDay, BBox b) {
            string dir = BakeDir(utcDay, b);
            string manFn = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manFn)) return false;

            var man = JsonSerializer.Deserialize<BakeManifest>(await File.ReadAllTextAsync(manFn));
            var rel = man?.preferred ?? man?.files?.FirstOrDefault();
            if (string.IsNullOrEmpty(rel)) return false;

            await PostOverlayAsync(rel!, b.ToArray());
            return true;
        }

        private async Task<bool> TryShowCachedTilesAsync(DateTime utcDay, BBox b) {
            string dir = BakeDir(utcDay, b);
            string manFn = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manFn)) return false;

            // быстрая проверка: есть ли хоть один тайл
            string tilesRoot = TilesDir(utcDay, b);
            if (!Directory.Exists(tilesRoot)) return false;

            // ок — постим команду на фронт
            await PostTileLayerAsync(utcDay, b);
            return true;
        }


        private static string PreferFirst(IEnumerable<string> rels) {
            // чуть «умнее»: Sentinel (HLSS30) приоритетнее Landsat (HLSL30)
            return rels.OrderByDescending(r => r.Contains("HLSS30", StringComparison.OrdinalIgnoreCase))
                       .ThenBy(r => r) // детерминизм
                       .First();
        }


        private async Task BakeDayAsync(
    List<(string id, string url, string band)> hrefs,
    DateTime utcDay, BBox b,
    IProgress<ProgressEvent> progress,
    CancellationToken ct) {
            string outDir = BakeDir(utcDay, b);
            Directory.CreateDirectory(outDir);

            // пекём по уникальным сценам
            var ids = hrefs.Select(h => h.id).Distinct().ToList();
            int total = ids.Count, done = 0;

            var rels = new List<string>();

            foreach (var id in ids) {
                ct.ThrowIfCancellationRequested();
                progress.Report(new(Stage.Bake, done, total, id));

                var (rel, abs) = await BuildNdviOverlayPngForId(
                    id,
                    outDir,
                    report: (c, t, note) => {
                        // внутри одной сцены можно не «дёргать» прогресс слишком часто
                        // но оставим короткий статус:
                        progress.Report(new(Stage.Bake, done, total, $"{id} {note}"));
                    },
                    ct: ct);

                rels.Add(rel);
                done++;
                progress.Report(new(Stage.Bake, done, total, Path.GetFileName(rel)));
            }

            // манифест
            WriteBakeManifest(outDir, BboxKey(b), utcDay, rels);

            // покажем предпочтительный слой
            var preferred = PreferFirst(rels);
            await PostOverlayAsync(preferred, b.ToArray());
        }

        private unsafe void RenderTileFromNdvi(
    float[] ndviSm, int w, int h, BBox b,
    (double lo, double hi) clip, Stop[] ramp, double gamma,
    string outPngAbs, int z, int x, int y) {
            Directory.CreateDirectory(Path.GetDirectoryName(outPngAbs)!);

            using var bmp = new Bitmap(TILE_SIZE, TILE_SIZE, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, TILE_SIZE, TILE_SIZE), ImageLockMode.WriteOnly, bmp.PixelFormat);

            // helpers
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static double MercYToLat(double v01) // v01 in [0..1] along Y in WebMercator
                => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * v01))) * 180.0 / Math.PI;

            double n = Math.Pow(2, z);
            double invN = 1.0 / n;
            double invTile = 1.0 / TILE_SIZE;

            double dLon = b.MaxLon - b.MinLon;
            double dLat = b.MaxLat - b.MinLat;

            byte* row = (byte*)data.Scan0;
            for (int py = 0; py < TILE_SIZE; py++, row += data.Stride) {
                byte* p = row;

                // глобальная нормированная координата Y всего мира (0..1)
                double yGlob01 = (y + (py + 0.5) * invTile) * invN;   // в долях высоты мира
                double lat = MercYToLat(yGlob01);

                for (int pxIdx = 0; pxIdx < TILE_SIZE; pxIdx++) {
                    // глобальная нормированная координата X всего мира (0..1)
                    double xGlob01 = (x + (pxIdx + 0.5) * invTile) * invN;
                    double lon = xGlob01 * 360.0 - 180.0;

                    // вне исходного bbox → прозрачный пиксель
                    if (lon < b.MinLon || lon > b.MaxLon || lat < b.MinLat || lat > b.MaxLat) {
                        p[0] = p[1] = p[2] = 0; p[3] = 0; p += 4;
                        continue;
                    }

                    // lon/lat → индексы исходного растрового клипа (в пикселях)
                    double fx = (lon - b.MinLon) / dLon * (w - 1);
                    double fy = (b.MaxLat - lat) / dLat * (h - 1);

                    int ix = (int)fx;
                    int iy = (int)fy;

                    float vNdvi = float.NaN;

                    if (ix >= 0 && ix < w && iy >= 0 && iy < h) {
                        // билинейная интерполяция с «затыканием» NaN ближайшими валидными
                        int ix1 = Math.Min(w - 1, ix + 1);
                        int iy1 = Math.Min(h - 1, iy + 1);
                        double dx = fx - ix;
                        double dy = fy - iy;

                        float v00 = ndviSm[iy * w + ix];
                        float v10 = ndviSm[iy * w + ix1];
                        float v01 = ndviSm[iy1 * w + ix];
                        float v11 = ndviSm[iy1 * w + ix1];

                        if (!float.IsFinite(v00) && float.IsFinite(v10)) v00 = v10;
                        if (!float.IsFinite(v00) && float.IsFinite(v01)) v00 = v01;
                        if (!float.IsFinite(v00) && float.IsFinite(v11)) v00 = v11;

                        if (float.IsFinite(v00) || float.IsFinite(v10) || float.IsFinite(v01) || float.IsFinite(v11)) {
                            double s10 = float.IsFinite(v10) ? v10 : v00;
                            double s01 = float.IsFinite(v01) ? v01 : v00;
                            double s11 = float.IsFinite(v11) ? v11 : v00;

                            double top = (1 - dx) * (float.IsFinite(v00) ? v00 : s10) + dx * s10;
                            double bot = (1 - dx) * (float.IsFinite(v01) ? v01 : s11) + dx * s11;
                            vNdvi = (float)((1 - dy) * top + dy * bot);
                        }
                    }

                    if (!float.IsFinite(vNdvi)) {
                        p[0] = p[1] = p[2] = 0; p[3] = 0; p += 4;
                        continue;
                    }

                    // нормализация → гамма → палитра
                    double t = (vNdvi - clip.lo) / (clip.hi - clip.lo);
                    if (t < 0) t = 0; if (t > 1) t = 1;
                    t = Math.Pow(t, gamma);

                    Stop s0 = ramp[0], s1 = ramp[^1];
                    for (int i = 1; i < ramp.Length; i++)
                        if (t <= ramp[i].v) { s0 = ramp[i - 1]; s1 = ramp[i]; break; }
                    double dt = (t - s0.v) / Math.Max(1e-9, (s1.v - s0.v));
                    var (r8, g8, b8, a8) = Lerp(s0, s1, dt);

                    // глобальная прозрачность
                    byte a = (byte)Math.Clamp((int)Math.Round(a8 * HEATMAP_ALPHA_SCALE), 0, 255);

                    p[0] = b8; p[1] = g8; p[2] = r8; p[3] = a;
                    p += 4;
                }
            }

            bmp.UnlockBits(data);
            bmp.Save(outPngAbs, ImageFormat.Png);
        }



        // ===== Paths/keys =====
        private string BakesRoot => Path.Combine(AppContext.BaseDirectory, "data", "bakes");

        private static string DateKey(DateTime utc) => utc.ToString("yyyyMMdd");

        private static string BboxKey(BBox b) {
            // компактный стабильный ключ; можно md5, но округления хватает
            string f(double v) => v.ToString("F5", System.Globalization.CultureInfo.InvariantCulture);
            return $"{f(b.MinLon)}_{f(b.MinLat)}_{f(b.MaxLon)}_{f(b.MaxLat)}";
        }

        private string BakeDir(DateTime utc, BBox b) =>
            Path.Combine(BakesRoot, BboxKey(b), DateKey(utc));


        private record BakeManifest(
            string dateUtc, string bboxKey, string[] files, string? preferred // какую картинку показывать первой
        );



        private record ZoomRange(int z, int xMin, int xMax, int yMin, int yMax);

        private record TileBakeManifest(
            string dateUtc,
            string bboxKey,
            ZoomRange[] zooms,
            string palette,
            int version = 1
        );

        private void WriteTileManifest(string dirAbs, DateTime utc, BBox b, IEnumerable<ZoomRange> zooms) {
            var man = new TileBakeManifest(
                dateUtc: utc.ToString("yyyy-MM-dd"),
                bboxKey: BboxKey(b),
                zooms: zooms.ToArray(),
                palette: "ir-heat-v1",
                version: 1
            );
            File.WriteAllText(Path.Combine(dirAbs, "manifest.json"),
                JsonSerializer.Serialize(man, new JsonSerializerOptions { WriteIndented = true }));
        }


        private string TilesDir(DateTime utc, BBox b) =>
            Path.Combine(BakeDir(utc, b), "z");

        private static string TilePath(string tilesRoot, int z, int x, int y) =>
            Path.Combine(tilesRoot, z.ToString(), x.ToString(), y.ToString() + ".png");

        //// относительный URL от корня BakesRoot
        //private string TileRelUrl(DateTime utc, BBox b, int z, int x, int y) {
        //    string abs = TilePath(TilesDir(utc, b), z, x, y);
        //    return Path.GetRelativePath(BakesRoot, abs).Replace(Path.DirectorySeparatorChar, '/');
        //}


        // ---- Tiles / WebMercator helpers ----
        private const int TILE_SIZE = 256;

        private const int Z_MIN = 8;   // подстрой по вкусу
        private const int Z_MAX = 14;  // подстрой по вкусу

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double LonToMercX(double lon) => (lon + 180.0) / 360.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double LatToMercY(double lat) {
            double s = Math.Sin(lat * Math.PI / 180.0);
            double y = 0.5 - Math.Log((1 + s) / (1 - s)) / (4 * Math.PI);
            return y;
        }

        private static (double minLon, double minLat, double maxLon, double maxLat) TileBounds(int z, int x, int y) {
            double n = Math.Pow(2, z);
            double lon1 = x / n * 360.0 - 180.0;
            double lat1 = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n))) * 180.0 / Math.PI;
            double lon2 = (x + 1) / n * 360.0 - 180.0;
            double lat2 = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (y + 1) / n))) * 180.0 / Math.PI;
            // south<north, west<east
            return (lon1, lat2, lon2, lat1);
        }


        private static (int xMin, int xMax, int yMin, int yMax) TilesCoveringBBox(int z, BBox b) {
            double n = Math.Pow(2, z);
            double latMin = Math.Max(-85.05112878, Math.Min(85.05112878, b.MinLat));
            double latMax = Math.Max(-85.05112878, Math.Min(85.05112878, b.MaxLat));

            int xMin = (int)Math.Floor((b.MinLon + 180.0) / 360.0 * n);
            int xMax = (int)Math.Floor((b.MaxLon + 180.0) / 360.0 * n);
            int yMin = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latMax * Math.PI / 180.0) + 1.0 / Math.Cos(latMax * Math.PI / 180.0)) / Math.PI) / 2.0 * n);
            int yMax = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latMin * Math.PI / 180.0) + 1.0 / Math.Cos(latMin * Math.PI / 180.0)) / Math.PI) / 2.0 * n);

            int last = (int)n - 1;
            return (
                Math.Max(0, xMin),
                Math.Min(last, xMax),
                Math.Max(0, yMin),
                Math.Min(last, yMax)
            );
        }


    }
}

