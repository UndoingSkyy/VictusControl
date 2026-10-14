using System.IO;

namespace VictusControl.App;

internal static class AppearancePreferences {
    private static readonly string MotionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VictusControl", "motion.txt");

    internal static bool LoadMotion() {
        try { return !File.Exists(MotionPath) || File.ReadAllText(MotionPath).Trim() != "off"; }
        catch { return true; }
    }

    internal static void SaveMotion(bool enabled) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(MotionPath)!);
            File.WriteAllText(MotionPath, enabled ? "on" : "off");
        } catch {
            // Preference persistence should never block the hardware dashboard.
        }
    }
}
