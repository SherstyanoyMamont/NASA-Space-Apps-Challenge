using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NASA_Space_Apps_Challenge {
    public static class AppeearsTaskBuilder {
        // ---- публичный хелпер: вернёт JSON тела запроса /api/task ----
        public static string BuildTaskJson(
          string taskName,
          double minLon, double minLat, double maxLon, double maxLat,
          DateTime start, DateTime end,
          bool includeS2 = false)   // ← по умолчанию выключено
      {
            var layers = new List<object>();

            // Landsat — всегда
            layers.AddRange(new[] {
                new { product = "HLSL30.002", layer = "B02" },
                new { product = "HLSL30.002", layer = "B03" },
                new { product = "HLSL30.002", layer = "B04" },
                new { product = "HLSL30.002", layer = "B05" },
                new { product = "HLSL30.002", layer = "Fmask" },
            });

            // Sentinel-2 — только если явно попросили (и когда снова станет доступен)
            if (includeS2) {
                layers.AddRange(new[] {
            new { product = "HLSS30.002", layer = "B02" },
            new { product = "HLSS30.002", layer = "B03" },
            new { product = "HLSS30.002", layer = "B04" },
            new { product = "HLSS30.002", layer = "B08" },
            new { product = "HLSS30.002", layer = "Fmask" },
        });
            }

            var task = new {
                task_type = "area",
                task_name = taskName,
                @params = new {
                    dates = new[] {
                new {
                    startDate = start.ToString("MM-dd-yyyy"),
                    endDate   = end.ToString("MM-dd-yyyy"),
                    recurring = false,
                    yearRange = new[]{1950,2050}
                }
            },
                    layers,
                    geo = new {
                        type = "FeatureCollection",
                        fileName = "bbox",
                        features = new[] {
                    new {
                        type = "Feature",
                        properties = new { },
                        geometry = new {
                            type = "Polygon",
                            coordinates = new [] {
                                new [] {
                                    new [] { minLon, minLat },
                                    new [] { minLon, maxLat },
                                    new [] { maxLon, maxLat },
                                    new [] { maxLon, minLat },
                                    new [] { minLon, minLat }
                                }
                            }
                        }
                    }
                }
                    },
                    output = new { format = new { type = "geotiff" }, projection = "geographic" }
                }
            };

            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null };
            return System.Text.Json.JsonSerializer.Serialize(task, opts)
                .Replace("\"@params\"", "\"params\"");
        }


        public static string BuildTaskJsonGeneric(
    string taskName,
    double minLon, double minLat, double maxLon, double maxLat,
    DateTime start, DateTime end,
    (string product, string[] layers, bool isHls) pick) {
            // если HLS — добавляем несколько слоёв одного продукта,
            // если VIIRS/MODIS — NDVI/EVI/Pixel_Reliability одного продукта
            var layers = pick.layers.Select(l => new { product = pick.product, layer = l }).ToArray();

            var task = new {
                task_type = "area",
                task_name = taskName,
                @params = new {
                    dates = new[] { new {
                startDate = start.ToString("MM-dd-yyyy"),
                endDate   = end.ToString("MM-dd-yyyy"),
                recurring = false, yearRange = new[]{1950,2050}
            }},
                    layers,
                    geo = new {
                        type = "FeatureCollection",
                        fileName = "bbox",
                        features = new[] {
                    new {
                        type = "Feature", properties = new { },
                        geometry = new {
                            type = "Polygon",
                            coordinates = new [] {
                                new [] {
                                    new [] { minLon, minLat },
                                    new [] { minLon, maxLat },
                                    new [] { maxLon, maxLat },
                                    new [] { maxLon, minLat },
                                    new [] { minLon, minLat }
                                }
                            }
                        }
                    }
                }
                    },
                    output = new { format = new { type = "geotiff" }, projection = "geographic" }
                }
            };

            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null };
            return System.Text.Json.JsonSerializer.Serialize(task, opts)
                .Replace("\"@params\"", "\"params\"");
        }

        public static string BuildTaskJsonAuto(
    bool hlsWanted,
    double minLon, double minLat, double maxLon, double maxLat,
    DateTime start, DateTime end,
    bool includeEVI = true) {
            var layers = new List<object>();

            if (hlsWanted) {
                // попытаемся HLSL30.002
                layers.AddRange(new[] {
            new { product = "HLSL30.002", layer = "B02" },
            new { product = "HLSL30.002", layer = "B03" },
            new { product = "HLSL30.002", layer = "B04" },
            new { product = "HLSL30.002", layer = "B05" },
            new { product = "HLSL30.002", layer = "Fmask" }
        });
            }
            else {
                // VIIRS Vegetation Indices (VNP13A1.061): NDVI (+EVI)
                layers.Add(new { product = "VNP13A1.061", layer = "NDVI" });
                if (includeEVI) layers.Add(new { product = "VNP13A1.061", layer = "EVI" });
                // можно подтянуть Pixel_Reliability/VI_Quality, если нужно фильтровать
                layers.Add(new { product = "VNP13A1.061", layer = "Pixel_Reliability" });
            }

            var task = new {
                task_type = "area",
                task_name = hlsWanted ? "HLS-bbox-demo" : "VIIRS-bbox-demo",
                @params = new {
                    dates = new[] { new {
                startDate = start.ToString("MM-dd-yyyy"),
                endDate   = end.ToString("MM-dd-yyyy"),
                recurring = false,
                yearRange = new[]{1950,2050}
            }},
                    layers,
                    geo = new {
                        type = "FeatureCollection",
                        fileName = "bbox",
                        features = new[] {
                    new {
                        type = "Feature",
                        properties = new { },
                        geometry = new {
                            type = "Polygon",
                            coordinates = new [] {
                                new [] {
                                    new [] { minLon, minLat },
                                    new [] { minLon, maxLat },
                                    new [] { maxLon, maxLat },
                                    new [] { maxLon, minLat },
                                    new [] { minLon, minLat }
                                }
                            }
                        }
                    }
                }
                    },
                    output = new { format = new { type = "geotiff" }, projection = "geographic" }
                }
            };

            var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = null };
            return System.Text.Json.JsonSerializer.Serialize(task, opts)
                .Replace("\"@params\"", "\"params\"");
        }


        // ---- DTO под структуру AppEEARS ----
        public class AreaTask {
            public string task_type { get; set; }
            public string task_name { get; set; }
            [JsonPropertyName("params")] public Params @params { get; set; }
        }
        public class Params {
            public DateRange[] dates { get; set; }
            public List<Layer> layers { get; set; }
            public FeatureCollection geo { get; set; }
            public Output output { get; set; }
        }
        public class DateRange {
            public string startDate { get; set; }  // "MM-dd-yyyy"
            public string endDate { get; set; }  // "MM-dd-yyyy"
            public bool recurring { get; set; }
            public int[] yearRange { get; set; }
        }
        public class Layer {
            public string product { get; set; }   // "HLSS30.002" | "HLSL30.002"
            public string layer { get; set; }   // "B02","B03","B04","B08/B05","Fmask"
        }
        public class Output {
            public string file_type { get; set; }        // "geotiff"
            public string projection_name { get; set; }  // "geographic" (EPSG:4326)
        }

        // ---- GeoJSON типов достаточно минимальных ----
        public class FeatureCollection {
            public string type { get; set; }             // "FeatureCollection"
            public Feature[] features { get; set; }
            public string fileName { get; set; }         // AppEEARS принимает это поле
        }
        public class Feature {
            public string type { get; set; }             // "Feature"
            public object properties { get; set; }
            public Geometry geometry { get; set; }
        }
        public class Geometry {
            public string type { get; set; }             // "Polygon"
            public double[][][] coordinates { get; set; }
        }
    }
}
