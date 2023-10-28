using FrooxEngine;
using HarmonyLib; // HarmonyLib comes included with a ResoniteModLoader install
using ResoniteModLoader;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace ResoniteSubtitleImporter
{

    public class ResoniteSubtitleImporter : ResoniteMod
    {
        public override string Name => "ResoniteSubtitleImporter";
        public override string Author => "Jackybuns";
        public override string Version => "0.0.1"; //Version of the mod, should match the AssemblyVersion
        public override string Link => "https://github.com/YourNameHere/ExampleMod"; // Optional link to a repo where this mod would be located

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> enabled = new ModConfigurationKey<bool>("import subtitles", "If subtitles should be imported", () => true); //Optional config settings

        private static ModConfiguration Config;//If you use config settings, this will be where you interface with them


        public override void OnEngineInit()
        {
            Config = GetConfiguration(); //Get this mods' current ModConfiguration
            Config.Save(true); //If you'd like to save the default config values to file
            Harmony.DEBUG = true;
            Harmony harmony = new Harmony("com.jackybuns.SubtitleImporter"); //typically a reverse domain name is used here (https://en.wikipedia.org/wiki/Reverse_domain_name_notation)
            harmony.PatchAll(); // do whatever LibHarmony patching you need, this will patch all [HarmonyPatch()] instances

            //Various log methods provided by the mod loader, below is an example of how they will look
            //3:14:42 AM.069 ( -1 FPS)  [INFO] [ResoniteModLoader/ExampleMod] a regular log
            /*
            Debug("a debug log");
            Msg("a regular log");
            Warn("a warn log");
            Error("an error log");
            */
        }

        // 	private static async Task ImportAsync(Slot slot, string path, string name, VideoType type, StereoLayout stereo, DepthPreset depth)
        [HarmonyPatch(typeof(VideoImportDialog), "ImportAsync")]
        class VideoImporterDialogPath
        {
            static void Postfix(Slot slot, string path)
            {
                if (!Config.GetValue(enabled))
                    return;

                slot.World.Coroutines.StartTask(async () =>
                {
                    Msg("Importing subtitles");
                    await default(ToBackground);
                    Uri uri = new Uri(path);
                    if (uri.Scheme == "file")
                    {
                        var info = await FFmpeg.GetMediaInfo(path);

                        foreach (var subtitle in info.SubtitleStreams)
                        {
                            var name = Path.GetFileName(path) + "-" + subtitle.Language + ".srt";
                            var outputPath = Path.Combine(Path.GetDirectoryName(path), name);
                            Msg(outputPath);
                            await FFmpeg.Conversions.New()
                                .AddStream(subtitle)
                                .SetOutput(outputPath)
                                .Start();
                        }

                    }
                    await default(ToWorld);
                });
            }

        }

    }
}
