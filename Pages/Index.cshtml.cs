using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RAMBHA_LP_Explorer.Models;
using RAMBHA_LP_Explorer.Services;
using System.Text.Json;

namespace RAMBHA_LP_Explorer.Pages
{
    public class IndexModel : PageModel
    {
        private readonly LangmuirAnalyzer _analyzer;

        public IndexModel(LangmuirAnalyzer analyzer)
        {
            _analyzer = analyzer;
        }

        [BindProperty]
        public IFormFile? Upload { get; set; }

        public string? ErrorMessage { get; private set; }
        public string? InfoMessage { get; private set; }

        public AnalysisResult? Analysis { get; private set; }
        public List<IvPoint> Points { get; private set; } = new();

        // >>> Added: JSON for the chart <<<
        public string PointsJson { get; private set; } = "[]";   // voltages
        public string CurrentsJson { get; private set; } = "[]"; // currents

        public void OnGet()
        {
            InfoMessage = "Upload a CSV with two columns: V (volts), I (amps). Headers allowed.";
            // Keep chart bindings valid even before upload
            PointsJson = "[]";
            CurrentsJson = "[]";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (Upload == null || Upload.Length == 0)
            {
                ErrorMessage = "Please choose a CSV file.";
                PointsJson = "[]";
                CurrentsJson = "[]";
                return Page();
            }

            try
            {
                using var stream = Upload.OpenReadStream();
                using var reader = new StreamReader(stream);

                var lines = new List<string>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (line != null) lines.Add(line);
                }

                Points = ParseCsvToPoints(lines);
                if (Points.Count < 5)
                {
                    ErrorMessage = $"Only {Points.Count} valid points found. Need at least 5.";
                    PointsJson = JsonSerializer.Serialize(Array.Empty<double>());
                    CurrentsJson = JsonSerializer.Serialize(Array.Empty<double>());
                    return Page();
                }

                // >>> Added: serialize X/Y arrays for the chart <<<
                PointsJson = JsonSerializer.Serialize(Points.Select(p => p.V));
                CurrentsJson = JsonSerializer.Serialize(Points.Select(p => p.I));

                // Run analysis
                Analysis = _analyzer.Analyze(Points);
                if (Analysis?.PointCount < 5)
                {
                    ErrorMessage = "Not enough usable data for analysis.";
                    return Page();
                }

                InfoMessage = $"Loaded {Analysis.PointCount} points.";
                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to parse or analyze file: " + ex.Message;
                PointsJson = "[]";
                CurrentsJson = "[]";
                return Page();
            }
        }

        private static List<IvPoint> ParseCsvToPoints(List<string> lines)
        {
            var pts = new List<IvPoint>();
            if (lines.Count == 0) return pts;

            // Detect delimiter on the first non-empty line
            string? firstNonEmpty = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (firstNonEmpty == null) return pts;

            char delim = DetectDelimiter(firstNonEmpty);

            bool headerSkipped = false;
            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = raw.Split(delim);
                if (parts.Length < 2)
                {
                    // Try whitespace split if delimiter guess fails
                    parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                }

                // Try parse first two columns as doubles (InvariantCulture)
                if (!TryParseDouble(parts[0], out double v) || !TryParseDouble(parts[1], out double i))
                {
                    // Likely a header row (skip only once)
                    if (!headerSkipped)
                    {
                        headerSkipped = true;
                        continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                pts.Add(new IvPoint(v, i));
            }

            // Sort and return
            pts = pts
                .Where(p => double.IsFinite(p.V) && double.IsFinite(p.I))
                .OrderBy(p => p.V)
                .ToList();

            return pts;
        }

        private static char DetectDelimiter(string sample)
        {
            if (sample.Contains(',')) return ',';
            if (sample.Contains(';')) return ';';
            if (sample.Contains('\t')) return '\t';
            return ','; // default
        }

        private static bool TryParseDouble(string s, out double value)
        {
            s = (s ?? string.Empty).Trim();
            return double.TryParse(
                s,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }
}