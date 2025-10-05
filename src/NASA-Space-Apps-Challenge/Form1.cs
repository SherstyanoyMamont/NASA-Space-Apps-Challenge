using System.Net.Http.Headers;
using System.Net.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;



namespace NASA_Space_Apps_Challenge
{
    public partial class Form1 : Form
    {

        private string googleApiKey = "AIzaSyAvUxaijMF39FQe9vU5Gz3XCOb5NTPEPWY"; // твой Google API Key

        private int currentOpacity = 50;
        private string currentType = "TREE_UPI";
        private DateTime selectedDate = DateTime.Today;
        private HttpClient client = new HttpClient();

        private string cacheDir = Path.Combine(Application.StartupPath, "pollen_cache");

        private async Task<string> CacheTileAsync(int zoom, int x, int y)
        {
            string tileDir = Path.Combine(cacheDir, zoom.ToString());
            Directory.CreateDirectory(tileDir);

            string tilePath = Path.Combine(tileDir, $"{x}_{y}.png");

            // Если уже скачан — просто возвращаем путь
            if (File.Exists(tilePath))
                return tilePath;

            // Иначе качаем из API
            string url = $"https://pollen.googleapis.com/v1/mapTypes/{currentType}/heatmapTiles/{zoom}/{x}/{y}?key={googleApiKey}";
            try
            {
                using (HttpClient http = new HttpClient())
                {
                    var bytes = await http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(tilePath, bytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки тайла {zoom}/{x}/{y}: {ex.Message}");
            }

            return tilePath;
        }


        public Form1()
        {
            InitializeComponent();

    
            // TrackBar
            trackBar1.Minimum = 0;
            trackBar1.Maximum = 100;
            trackBar1.Value = currentOpacity;
            trackBar1.Scroll += (s, e) => { currentOpacity = trackBar1.Value; InitMap(); };

            // ComboBox
            comboBox1.Items.AddRange(new string[] { "TREE_UPI", "GRASS_UPI", "WEED_UPI" });
            comboBox1.SelectedIndex = 0;
            comboBox1.SelectedIndexChanged += (s, e) => { currentType = comboBox1.SelectedItem.ToString(); InitMap(); };

            // DatePicker
            dateTimePicker1.ValueChanged += (s, e) => { selectedDate = dateTimePicker1.Value.Date; InitMap(); };

            InitMap();
        }



        private async void InitMap()
        {
            await webView21.EnsureCoreWebView2Async(null);
            double opacityValue = currentOpacity / 100.0;

            string html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                  <meta charset='utf-8'>
                  <style>
                    html, body {{ height: 100%; margin: 0; padding: 0; }}
                    #map {{ height: 100%; width: 100%; }}
                    .gm-style .gm-style-iw-c {{ box-shadow: none !important; }}
                  </style>
                  <script src='https://maps.googleapis.com/maps/api/js?key={googleApiKey}'></script>
                  <script>
                  function initMap() {{
                      var map = new google.maps.Map(document.getElementById('map'), {{
                          center: {{ lat: 51.5, lng: 5.0 }},
                          zoom: 5,
                          disableDefaultUI: true
                      }});

                      // === Слой пыльцы ===
                      var pollenLayer = new google.maps.ImageMapType({{
                          getTileUrl: function(coord, zoom) {{
                              return 'https://pollen.googleapis.com/v1/mapTypes/{currentType}/heatmapTiles/' 
                                  + zoom + '/' + coord.x + '/' + coord.y + '?key={googleApiKey}';
                          }},
                          tileSize: new google.maps.Size(256, 256),
                          opacity: {opacityValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}
                      }});
                      map.overlayMapTypes.insertAt(0, pollenLayer);

                      // === Попробуем загрузить контур Нидерландов ===
                      fetch('https://raw.githubusercontent.com/johan/world.geo.json/master/countries/NLD.geo.json')
                          .then(response => {{
                              if (!response.ok) throw new Error('GeoJSON not found');
                              return response.json();
                          }})
                          .then(data => {{
                              map.data.addGeoJson(data);
                  
                              map.data.setStyle({{
                                  fillColor: '#888888',
                                  fillOpacity: 0.3,
                                  strokeColor: '#555555',
                                  strokeWeight: 2
                              }});

                              // Подсветка при наведении
                              map.data.addListener('mouseover', function(event) {{
                                  map.data.overrideStyle(event.feature, {{
                                      fillColor: '#bbbbbb',
                                      fillOpacity: 0.5,
                                      strokeWeight: 3
                                  }});
                              }});

                              map.data.addListener('mouseout', function(event) {{
                                  map.data.revertStyle();
                              }});

                              // При клике — открыть локальную форму
                              map.data.addListener('click', function(event) {{
                                  window.chrome.webview.postMessage('openLocalBloom');
                              }});
                          }})
                          .catch(error => {{
                              console.error('GeoJSON load error:', error);

                              // === Fallback: обычный прямоугольник, если GeoJSON не загрузился ===
                              const netherlandsBounds = {{
                                  north: 53.6,
                                  south: 50.7,
                                  west: 3.3,
                                  east: 7.2
                              }};

                              const rectangle = new google.maps.Rectangle({{
                                  strokeColor: '#888888',
                                  strokeOpacity: 0.9,
                                  strokeWeight: 2,
                                  fillColor: '#888888',
                                  fillOpacity: 0.25,
                                  map,
                                  bounds: netherlandsBounds
                              }});

                              rectangle.addListener('mouseover', () => {{
                                  rectangle.setOptions({{
                                      fillColor: '#bbbbbb',
                                      fillOpacity: 0.45,
                                      strokeWeight: 3
                                  }});
                              }});

                              rectangle.addListener('mouseout', () => {{
                                  rectangle.setOptions({{
                                      fillColor: '#888888',
                                      fillOpacity: 0.25,
                                      strokeWeight: 2
                                  }});
                              }});

                              rectangle.addListener('click', () => {{
                                  window.chrome.webview.postMessage('openLocalBloom');
                              }});
                          }});
                  }}
                  </script>
                </head>
                <body onload='initMap()'>
                  <div id='map'></div>
                </body>
                </html>";

            webView21.NavigateToString(html);

            // Обработка сообщений из JS
            webView21.CoreWebView2.WebMessageReceived += (sender, args) =>
            {
                if (args.TryGetWebMessageAsString() == "openLocalBloom")
                {
                    OpenLocalBloomForm();
                }
            };
        }


        private void OpenLocalBloomForm()
        {
            LocalBloomForm localForm = new LocalBloomForm();
            localForm.Show();
        }


        private async Task<string> GetPollenForecast(double lat, double lon, int days)
        {
            string url = $"https://pollen.googleapis.com/v1/forecast:lookup?key={googleApiKey}" +
                         $"&location.latitude={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                         $"&location.longitude={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                         $"&days={days}&languageCode=en&plantsDescription=true";

            try
            {
                var response = await client.GetStringAsync(url);
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching pollen forecast: " + ex.Message);
                return "{}"; // возвращаем пустой JSON, чтобы не падал парсер
            }
        }

        private string ParseForecastToJs(string json, int dayIndex)
        {
            try
            {
                var obj = JObject.Parse(json);
                var day = obj["dailyInfo"]?[dayIndex];
                if (day == null) return "var heatmapData = [];";

                var types = day["pollenTypeInfo"];
                StringBuilder jsData = new StringBuilder("var heatmapData = [");

                string forecastType = currentType switch
                {
                    "TREE_UPI" => "TREE",
                    "GRASS_UPI" => "GRASS",
                    "WEED_UPI" => "WEED",
                    _ => "TREE"
                };

                foreach (var t in types)
                {
                    string type = t["code"]?.ToString();
                    if (type != forecastType) continue;

                    int value = t["indexInfo"]?["value"]?.ToObject<int>() ?? 0;
                    Random rnd = new Random();

                    for (int i = 0; i < 5; i++)
                    {
                        double lat = 32.287014 + (rnd.NextDouble() - 0.5) * 0.1;
                        double lon = -96.967893 + (rnd.NextDouble() - 0.5) * 0.1;
                        jsData.Append($@"{{location: new google.maps.LatLng({lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}), weight: {value}}},");
                    }
                }

                jsData.Append("];");
                return jsData.ToString();
            }
            catch
            {
                return "var heatmapData = [];";
            }
        }

        private async Task<(double lat, double lng)> GetCityCoordinates(string cityName)
        {
            string url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(cityName)}&key={googleApiKey}";
            var response = await client.GetStringAsync(url);
            var json = JObject.Parse(response);
            var location = json["results"]?.First?["geometry"]?["location"];
            if (location != null)
                return (location["lat"].Value<double>(), location["lng"].Value<double>());
            return (0, 0); // город не найден
        }

        public async Task<string> GetCityPollenForecastAsync(string cityName)
        {
            var (lat, lng) = await GetCityCoordinates(cityName);
            if (lat == 0 && lng == 0) return "City not found";

            string forecastJson = await GetPollenForecast(lat, lng, 1);
            Console.WriteLine("RESPONSE:");
            Console.WriteLine(forecastJson);

            var obj = JObject.Parse(forecastJson);
            var dayInfo = obj["dailyInfo"]?[0];
            if (dayInfo == null) return "No forecast available";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"City: {cityName}");

            foreach (var type in dayInfo["pollenTypeInfo"])
            {
                string name = type["displayName"]?.ToString() ?? type["code"]?.ToString() ?? "Unknown";
                string category = type["indexInfo"]?["category"]?.ToString() ?? "None";
                int value = type["indexInfo"]?["value"]?.ToObject<int>() ?? 0;

                sb.AppendLine($"{name}: {category} (UPI: {value})");
            }

            return sb.ToString();
        }

        private void ButtonCityForecast3_Click(object sender, EventArgs e)
        {
            CityForecastForm cityForm = new CityForecastForm(this);
            cityForm.Show();

        }




        /// <summary>
        /// ////////////////////////////////////////////////////////////////////
        /// </summary>


        private double minLon = 5.70;
        private double minLat = 52.60;
        private double maxLon = 5.90;
        private double maxLat = 52.75;


        /* EARTH LOGIN TOKEN */

        public readonly string EARTH_LOGIN_TOKEN =
            "eyJ0eXAiOiJKV1QiLCJvcmlnaW4iOiJFYXJ0aGRhdGEgTG9naW4iLCJzaWciOiJlZGxqd3RwdWJrZXlfb3BzIiwiYWxnIjoiUlMyNTYifQ.eyJ0eXBlIjoiVXNlciIsInVpZCI6ImFzY2VnIiwiZXhwIjoxNzY0NzI0Mzg3LCJpYXQiOjE3NTk1NDAzODcsImlzcyI6Imh0dHBzOi8vdXJzLmVhcnRoZGF0YS5uYXNhLmdvdiIsImlkZW50aXR5X3Byb3ZpZGVyIjoiZWRsX29wcyIsImFjciI6ImVkbCIsImFzc3VyYW5jZV9sZXZlbCI6M30.zPuUBGNgjzzeIbXzQu2Tt2B5XSG-VWUfQa8229fjLzEcRIi2dGKKIqofs1oFksOuYIKHky43WD8y6oce8yrIi4YgV5_5yjdDpeL2T7SMdmYbZxMUsI1rCYFRUuz3IBO8bJCE9E3JPqY9wewtTmY0XxjVKTBRkCu-q8Okzh-VZkoUlypnGgF_RvbEKt6DFVIrLmkVhYslIJ_ipdngLNSgVVV-ZeKZ3uT--h7NdhPcryukNWY5hkQ8mlfOjdhWYgBTz0RFPsLkEh7Pf0Ynk1EE7QS-Q_w_kl-I_pBNkYmE4SglS4PF-5s0PgmfH6iKC0ST91UB5605L5gL2pa7gcUBAQ";



        async void GetHLSData()
        {


            // Готовим клиент и адрес STAC-поиска
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", EARTH_LOGIN_TOKEN);


            var start = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var endExcl = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc); // 1 июня 00:00Z, эксклюзивно



            // Собираем тело запроса
            // collections: какие наборы хотим (HLS S2 и HLS Landsat).
            // bbox: прямоугольник WGS84 в порядке minLon,minLat,maxLon,maxLat.
            // datetime: интервал дат(ISO 8601).
            // limit: максимум элементов за один ответ(остальные придут через пагинацию links.next — здесь не обработано).

            var body = new
            {
                //  Ищем в коллекциях HLSS30 и HLSL30 с фильтром облачности
                // для Sentinel-2 (HLSS30) NIR — это B08,
                // для Landsat(HLSL30) NIR — это B05.
                collections = new[] { "HLSS30.v2.0", "HLSL30.v2.0" },
                // collections = new[] { "HLSS30", "HLSL30" },
                bbox = new[] { minLon, minLat, maxLon, maxLat },
                // use RFC3339 or ISO8601 formatted datetime strings
                datetime = $"{start:yyyy-MM-ddTHH:mm:ssZ}/{endExcl:yyyy-MM-ddTHH:mm:ssZ}",
                //datetime = "2025-04-01/2025-05-31",
                limit = 200
            };



            var bodyJson = JsonSerializer.Serialize(body);
            var req = new StringContent(bodyJson, Encoding.UTF8, "application/json");


            // теперь можно делать и поиск по STAC, и скачивание assets
            // отправляем запрос; если всё ок — забираем ответ как строку JSON.

            var searchUrl = "https://cmr.earthdata.nasa.gov/stac/LPCLOUD/search";
            var resp = await http.PostAsync(searchUrl, req);



            var respText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"STAC 400/..: {resp.StatusCode}\n{respText}");
            }



            //resp.EnsureSuccessStatusCode();
            //var json = await resp.Content.ReadAsStringAsync();


            // парсим
            using var doc = JsonDocument.Parse(respText);
            var features = doc.RootElement.GetProperty("features");

            // собираем ссылки на ассеты
            var hrefs = new List<(string id, string url, string band)>();
            foreach (var f in features.EnumerateArray())
            {
                var id = f.GetProperty("id").GetString();
                var assets = f.GetProperty("assets");

                // HLS v2: обычно есть B02,B03,B04, и NIR: B08 (S2) или B05 (L8/9).
                // Маска качества чаще называется "Fmask" (а не QA).
                foreach (var band in new[] { "B02", "B03", "B04", "B08", "B05", "Fmask" })
                {
                    if (assets.TryGetProperty(band, out var a) &&
                        a.TryGetProperty("href", out var href))
                    {
                        hrefs.Add((id!, href.GetString()!, band));
                    }
                }
            }



            /*


            // Парсим ответ и достаём ссылки на файлы(assets)
            // распарсить features → взять assets["B03","B04","B08"/"B05","B02","QA"]

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var features = doc.RootElement.GetProperty("features");
            var hrefs = new List<(string id, string url, string band)>();


            // в STAC каждый feature — это одна сцена/тайл.
            foreach (var f in features.EnumerateArray()) {

                // у неё есть словарь assets: ключи — названия полос (B02/B03/B04/…/QA),
                // значения — метаданные, где href — прямая ссылка на файл (обычно COG GeoTIFF).
                var assets = f.GetProperty("assets");
                foreach (var band in new[] { "B02", "B03", "B04", "B08", "B05", "QA" }) {

                    // мы проходим по сценам, проверяем, есть ли нужная полоса, и собираем (id, url, band) в список hrefs

                    if (assets.TryGetProperty(band, out var a) && a.TryGetProperty("href", out var href))
                        hrefs.Add((f.GetProperty("id").GetString(), href.GetString(), band));
                }
            }

            // что дальше с hrefs
            // — у вас на руках прямые URL на нужные файлы(B02 / B03 / B04 / NIR / QA).
            // Их можно скачивать и класть в data/, затем резать по bbox и считать индексы.

            /*
             
            если сервер вернёт 401/403, нужен Earthdata Login (куки/токен).
            чтобы отфильтровать облачность в самом поиске, можно добавить query (например, {"eo:cloud_cover":{"lt":40}}).
            результаты могут быть разбиты на страницы; для полного набора проверьте в ответе links → rel:"next" и повторяйте запрос.
            строка stacUrl = ".../collections/search" вам тут не нужна — реальная работа идёт через .../search.

             * */



        }


        static async Task<string> AppeearsLoginAsync(string user, string pass)
        {
            using var http = new HttpClient();
            var byteCreds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            var req = new HttpRequestMessage(HttpMethod.Post,
                "https://appeears.earthdatacloud.nasa.gov/api/login");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", byteCreds);
            req.Content = new StringContent("grant_type=client_credentials",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            var resp = await http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("token").GetString()!; // bearer
        }



        private void button1_Click(object sender, EventArgs e)
        {
            //GetHLSData();



            //var token = AppeearsLoginAsync("asceg", "In345-523-234Code");

            //File.WriteAllText("appeears_token", token.Result);

            LocalBloomForm form = new LocalBloomForm();
            form.ShowDialog();

        }

        private void webView21_Click(object sender, EventArgs e)
        {

        }

        
    }
}
