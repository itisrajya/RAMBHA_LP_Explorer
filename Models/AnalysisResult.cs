namespace RAMBHA_LP_Explorer.Models
{
    public class AnalysisResult
    {
        // Key Langmuir quick-look outputs (nullable when not enough data)
        public double? FloatingPotential_Vf { get; set; }
        public double? PlasmaPotential_Vp { get; set; }
        public double? ElectronTemperature_eV { get; set; }
        public double? ElectronSaturationCurrent_Amps { get; set; }

        // Helpful metadata
        public int PointCount { get; set; }
        public string Notes { get; set; } = "";
    }

}
