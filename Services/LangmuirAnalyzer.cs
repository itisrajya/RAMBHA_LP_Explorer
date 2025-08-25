using System.Globalization;
using RAMBHA_LP_Explorer.Models;
namespace RAMBHA_LP_Explorer.Services
{
    public class LangmuirAnalyzer
    {
        public AnalysisResult Analyze(IReadOnlyList<IvPoint> pointsIn)
        {
            var result = new AnalysisResult
            {
                PointCount = pointsIn?.Count ?? 0,
                Notes = "Quick-look analysis (zero-crossing Vf, max dI/dV Vp, ln(I) fit for Te, high-V tail average for Ie_sat)."
            };

            if (pointsIn == null || pointsIn.Count < 5)
                return result;

            var pts = pointsIn
                .Where(p => !double.IsNaN(p.V) && !double.IsNaN(p.I) && double.IsFinite(p.V) && double.IsFinite(p.I))
                .OrderBy(p => p.V)
                .ToList();

            if (pts.Count < 5) return result;

            // Deduplicate by V
            var dedup = new List<IvPoint>(pts.Count);
            double? lastV = null;
            foreach (var p in pts)
            {
                if (lastV is null || Math.Abs(p.V - lastV.Value) > 1e-12)
                {
                    dedup.Add(p);
                    lastV = p.V;
                }
            }
            pts = dedup;
            if (pts.Count < 5) return result;

            result.FloatingPotential_Vf = EstimateFloatingPotential(pts);
            result.PlasmaPotential_Vp = EstimatePlasmaPotential(pts);
            result.ElectronTemperature_eV = EstimateElectronTemperature(pts, result.FloatingPotential_Vf, result.PlasmaPotential_Vp);
            result.ElectronSaturationCurrent_Amps = EstimateElectronSaturationCurrent(pts, result.PlasmaPotential_Vp,
                                                                                      pts.First().V, pts.Last().V, Math.Max(1e-6, pts.Last().V - pts.First().V));
            return result;
        }

        private static double? EstimateFloatingPotential(List<IvPoint> pts)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                if (a.I == 0) return a.V;
                if (b.I == 0) return b.V;
                if ((a.I < 0 && b.I > 0) || (a.I > 0 && b.I < 0))
                {
                    double t = -a.I / (b.I - a.I);
                    return a.V + t * (b.V - a.V);
                }
            }
            var minAbs = pts.Select(p => (absI: Math.Abs(p.I), v: p.V)).OrderBy(x => x.absI).First();
            return minAbs.v;
        }

        private static double? EstimatePlasmaPotential(List<IvPoint> pts)
        {
            if (pts.Count < 3) return null;
            double maxSlope = double.NegativeInfinity;
            double vp = pts[0].V;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                double dv = pts[i + 1].V - pts[i].V;
                if (Math.Abs(dv) < 1e-12) continue;
                double slope = (pts[i + 1].I - pts[i].I) / dv;
                if (slope > maxSlope)
                {
                    maxSlope = slope;
                    vp = 0.5 * (pts[i].V + pts[i + 1].V);
                }
            }
            return double.IsFinite(maxSlope) ? vp : (double?)null;
        }

        private static double? EstimateElectronTemperature(List<IvPoint> pts, double? vf, double? vp)
        {
            if (vf is null || vp is null) return null;
            var vmin = Math.Min(vf.Value, vp.Value);
            var vmax = Math.Max(vf.Value, vp.Value);
            if (vmax - vmin < 1e-6) return null;

            double trim = 0.10 * (vmax - vmin);
            double low = vmin + trim, high = vmax - trim;

            var region = pts.Where(p => p.V >= low && p.V <= high && p.I > 0).ToList();
            if (region.Count < 3) return null;

            var x = region.Select(p => p.V).ToArray();
            var y = region.Select(p => Math.Log(p.I)).ToArray();

            if (!TryLinearFit(x, y, out double slope, out _)) return null;
            if (Math.Abs(slope) < 1e-12) return null;

            double te = 1.0 / slope;
            return double.IsFinite(te) ? te : (double?)null;
        }

        private static double? EstimateElectronSaturationCurrent(List<IvPoint> pts, double? vp, double minV, double maxV, double spanV)
        {
            double cutoff = (vp ?? (minV + 0.7 * spanV)) + 0.2 * spanV;
            var tail = pts.Where(p => p.V >= cutoff && p.I > 0).Select(p => p.I).ToList();
            if (tail.Count >= 3) return tail.Average();

            int take = Math.Max(3, pts.Count / 10);
            var alt = pts.Where(p => p.I > 0).OrderByDescending(p => p.V).Take(take).Select(p => p.I).ToList();
            if (alt.Count >= 3) return alt.Average();

            return null;
        }

        private static bool TryLinearFit(double[] x, double[] y, out double slope, out double intercept)
        {
            slope = 0; intercept = 0;
            int n = Math.Min(x.Length, y.Length);
            if (n < 2) return false;

            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                sx += x[i]; sy += y[i]; sxx += x[i] * x[i]; sxy += x[i] * y[i];
            }
            double denom = n * sxx - sx * sx;
            if (Math.Abs(denom) < 1e-20) return false;

            slope = (n * sxy - sx * sy) / denom;
            intercept = (sy - slope * sx) / n;
            return double.IsFinite(slope) && double.IsFinite(intercept);
        }
    }
}
