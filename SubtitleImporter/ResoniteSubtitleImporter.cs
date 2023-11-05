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
        public override string Version => "0.0.2"; //Version of the mod, should match the AssemblyVersion
        public override string Link => "https://github.com/jackybuns/ResoniteSubtitleImporter"; // Optional link to a repo where this mod would be located

        [AutoRegisterConfigKey]
        internal static readonly ModConfigurationKey<bool> enabled = new ModConfigurationKey<bool>("import subtitles", "If subtitles should be automatically imported on any video player that you spawn or set the URL of.", () => true); //Optional config settings

        [AutoRegisterConfigKey]
        internal static readonly ModConfigurationKey<bool> openInspector = new ModConfigurationKey<bool>("open inspector", "If an inspector should be opened on the imported subtitles (only manual import on VideoTextureProvider)", () => false); //Optional config settings

        [AutoRegisterConfigKey]
        internal static readonly ModConfigurationKey<bool> reparentToPlayer = new ModConfigurationKey<bool>("parent under player", "Try to parent subtitles under the video player, automatic imports will always try to parent themselves irregardless of this setting (only works if the palyer slot gets the same name as the video file)", () => true); //Optional config settings

        internal static ModConfiguration Config;//If you use config settings, this will be where you interface with them


        public override void OnEngineInit()
        {
            var path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "..");
            if (File.Exists(Path.Combine(path, "ffmpeg.exe")))
            {
                FFmpeg.SetExecutablesPath(path);
            }

            Config = GetConfiguration(); //Get this mods' current ModConfiguration
            Config.Save(true); //If you'd like to save the default config values to file
            //Harmony.DEBUG = true;
            Harmony harmony = new Harmony("com.jackybuns.SubtitleImporter"); //typically a reverse domain name is used here (https://en.wikipedia.org/wiki/Reverse_domain_name_notation)
            harmony.PatchAll(); // do whatever LibHarmony patching you need, this will patch all [HarmonyPatch()] instances
        }

    }
}
