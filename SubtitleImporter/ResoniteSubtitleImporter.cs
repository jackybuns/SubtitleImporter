using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using HarmonyLib; // HarmonyLib comes included with a ResoniteModLoader install
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace ResoniteSubtitleImporter
{

    public class ResoniteSubtitleImporter : ResoniteMod
    {
        public override string Name => "ResoniteSubtitleImporter";
        public override string Author => "Jackybuns (U-Jackson)";
        public override string Version => "0.0.1"; //Version of the mod, should match the AssemblyVersion
        public override string Link => "https://github.com/jackybuns/ResoniteSubtitleImporter"; // Optional link to a repo where this mod would be located

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> enabled = new ModConfigurationKey<bool>("import subtitles", "If subtitles should be imported", () => true); //Optional config settings

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> keepSubFiles = new ModConfigurationKey<bool>("keep subtitle files", "If subtitle files should be kept, otherwise they will be deleted", () => true); //Optional config settings

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> openInspector = new ModConfigurationKey<bool>("open inspector", "If an inspector should be opened on the imported subtitles", () => true); //Optional config settings

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> reparentToPlayer = new ModConfigurationKey<bool>("parent under player", "Try to parent subtitles under the video player (only works if the palyer slot gets the same name as the video file)", () => true); //Optional config settings

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

        //private static async Task ImportAsync(Slot slot, string path, string name, VideoType type, StereoLayout stereo, DepthPreset depth)
        //[HarmonyPatch(typeof(VideoImportDialog), "RunImport")]
        [HarmonyPatch(typeof(VideoImportDialog), "ImportAsync")]
        class VideoImporterDialogPatch
        {
            static void Prefix(Slot slot, ref World __state)
            {
                __state = slot.World;
            }

            static void Postfix(Slot slot, string path, ref Task __result, ref World __state)
            {
                if (!Config.GetValue(enabled))
                    return;

                //var path = __instance.Paths.First();
                var world = __state;
                var engine = world.Engine;
                var importTask = __result;

                world.Coroutines.StartTask(async delegate
                {
                    Uri uri = new Uri(path);
                    if (uri.Scheme == "file")
                    {
                        // await the video import task that we patch
                        // we need this so we can ensure that the video player was created already, otherwise we cannot parent the subtitles there
                        await importTask;

                        var subRootSlot = await ImportHelper.ImportSubtitles(path, world, Config.GetValue(reparentToPlayer), Config.GetValue(keepSubFiles));

                        await default(ToWorld);
                        if (Config.GetValue(openInspector))
                        {
                            DevCreateNewForm.OpenInspector(subRootSlot);
                        }
                    }
                });
            }

        }

    }
}
