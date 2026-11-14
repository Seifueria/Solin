// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // How the Mac is feeling: battery, charger, network, load. Polled every half minute; changes come out as events.
    // (Ideas described from mochiiwuff/capri-desktop's PC-feel reactions; no code taken.)
    internal sealed class Vitals
    {
        [DllImport("libSystem.dylib")] private static extern int getloadavg(double[] loads, int count);

        internal int BatteryPercent = -1;
        internal bool OnCharger = true, Online = true, Hot;
        private int lowWarned = 101;
        private DateTime hotSince = DateTime.MaxValue;
        private bool first = true;

        // kind: "unplugged", "plugged", "battery_low", "battery_critical", "offline", "online", "hot", "cooled"; detail for a line.
        internal event Action<string, string> Changed;

        internal async Task Poll()
        {
            try { await ReadBattery(); } catch (Exception) { }
            try { ReadNetwork(); } catch (Exception) { }
            try { ReadLoad(); } catch (Exception) { }
            first = false;
        }

        private async Task ReadBattery()
        {
            if (!OperatingSystem.IsMacOS()) return;
            ProcessStartInfo info = new ProcessStartInfo("/usr/bin/pmset", "-g batt") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (Process p = Process.Start(info))
            {
                string text = await p.StandardOutput.ReadToEndAsync();
                p.WaitForExit(2000);
                Match m = Regex.Match(text, @"(\d+)%;\s*([a-zA-Z ]+?)(;|$)", RegexOptions.Multiline);
                if (!m.Success) return;
                int percent = int.Parse(m.Groups[1].Value);
                string state = m.Groups[2].Value.Trim();
                bool charger = text.Contains("AC Power") || state == "charging" || state == "charged" || state == "AC attached" || state == "finishing charge";
                if (!first && charger != OnCharger) Changed?.Invoke(charger ? "plugged" : "unplugged", charger ? "charger's back in" : "you pulled the plug, " + percent + "% left");
                OnCharger = charger;
                BatteryPercent = percent;
                if (!charger && percent <= 8 && lowWarned > 8) { lowWarned = 8; Changed?.Invoke("battery_critical", percent + "% left. plug me in, like, now"); }
                else if (!charger && percent <= 20 && lowWarned > 20) { lowWarned = 20; Changed?.Invoke("battery_low", percent + "% battery. heads up"); }
                if (charger || percent > 25) lowWarned = 101;
            }
        }

        private void ReadNetwork()
        {
            bool online = NetworkInterface.GetIsNetworkAvailable();
            if (!first && online != Online) Changed?.Invoke(online ? "online" : "offline", online ? "we're back online" : "network's gone");
            Online = online;
        }

        private void ReadLoad()
        {
            if (!OperatingSystem.IsMacOS()) return;
            double[] loads = new double[3];
            if (getloadavg(loads, 3) < 1) return;
            int cores = Math.Max(1, Environment.ProcessorCount);
            bool busy = loads[0] > cores * 1.4;
            if (busy && hotSince == DateTime.MaxValue) hotSince = DateTime.Now;
            if (!busy) hotSince = DateTime.MaxValue;
            bool hot = busy && (DateTime.Now - hotSince).TotalSeconds > 90;
            if (hot && !Hot) Changed?.Invoke("hot", "this thing is working hard. load " + loads[0].ToString("0.0") + " on " + cores + " cores");
            if (!hot && Hot && !busy) Changed?.Invoke("cooled", "cooled off");
            Hot = hot;
        }
    }
}
