﻿using Elements.Assets;
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

        [HarmonyPatch(typeof(VideoImportDialog), "RunImport")]
        class VideoImporterDialogPatch
        {
            static void Prefix(VideoImportDialog __instance)
            {
                if (!Config.GetValue(enabled))
                    return;

                var path = __instance.Paths.First();
                var world = __instance.World;
                var engine = __instance.Engine;

                __instance.World.Coroutines.StartTask(async delegate
                {
                    var filename = Path.GetFileNameWithoutExtension(path);
                    Msg("Importing subtitles");
                    await default(ToWorld);
                    var subRootSlot = world.LocalUserSpace.AddSlot("Subtitles - " + filename);
                    await default(ToBackground);
                    Uri uri = new Uri(path);
                    if (uri.Scheme == "file")
                    {
                        var info = await FFmpeg.GetMediaInfo(path);

                        Msg($"Importing {info.SubtitleStreams.Count()} subtitles");
                        var i = 0;
                        foreach (var subtitle in info.SubtitleStreams)
                        {
                            var subname = i + "-" + (string.IsNullOrEmpty(subtitle.Title) ? subtitle.Language : subtitle.Title);

                            var nameVtt = String.Format("{0}-{1}.vtt",
                                filename, subname);
                            var nameSrt = String.Format("{0}-{1}.srt",
                                filename, subname);
                            var outputPathVtt = Path.Combine(Path.GetDirectoryName(path), nameVtt);
                            var outputPathSrt = Path.Combine(Path.GetDirectoryName(path), nameSrt);

                            // Do an additional conversion into the vtt format as that strips all unneeded html tags
                            var result = await FFmpeg.Conversions.New()
                                .AddStream(subtitle)
                                .SetOutput(outputPathVtt)
                                .SetOverwriteOutput(true)
                                .Start();

                            Msg($"VTT Subtitle conversion took {(result.EndTime - result.StartTime).TotalSeconds} seconds");

                            var vttinfo = await FFmpeg.GetMediaInfo(outputPathVtt);
                            result = await FFmpeg.Conversions.New()
                                .AddStream(vttinfo.SubtitleStreams.First())
                                .SetOutput(outputPathSrt)
                                .SetOverwriteOutput(true)
                                .Start();

                            Msg($"SRT Subtitle conversion took {(result.EndTime - result.StartTime).TotalSeconds} seconds");

                            // cleanup vtt file
                            if (File.Exists(outputPathVtt))
                                File.Delete(outputPathVtt);


                            // import sub
                            Msg("importing subtitle " + subname);
                            await default(ToBackground);
                            AnimX anim = SubtitleImporter.Import(outputPathSrt);
                            if (anim == null)
                            {
                                Error("imported subtitle animation is null");
                                continue;
                            }
                            Uri subUri = await engine.LocalDB.SaveAssetAsync(anim);
                            if (subUri == null)
                            {
                                Error("subtitle animation asset uri is null");
                                continue;
                            }
                            await default(ToWorld);
                            var subSlot = subRootSlot.AddSlot(subname);
                            var animProvider = subSlot.AttachComponent<StaticAnimationProvider>();
                            animProvider.URL.Value = subUri;
                            var animator = subSlot.AttachComponent<Animator>();
                            animator.Clip.Target = animProvider;

                            var dynVarSpace = subSlot.AttachComponent<DynamicVariableSpace>();
                            dynVarSpace.SpaceName.Value = "Subtitle";
                            dynVarSpace.OnlyDirectBinding.Value = true;

                            var dynUri = subSlot.AttachComponent<DynamicValueVariable<Uri>>();
                            dynUri.VariableName.Value = "Subtitle/Uri";

                            var copy = subSlot.AttachComponent<ValueCopy<Uri>>();
                            copy.Source.Value = animProvider.URL.ReferenceID;
                            copy.Target.Value = dynUri.Value.ReferenceID;

                            var text = subSlot.AttachComponent<DynamicValueVariable<string>>();
                            text.VariableName.Value = "Subtitle/Text";
                            AssetProxy<FrooxEngine.Animation> assetProxy = subSlot.AttachComponent<AssetProxy<FrooxEngine.Animation>>();
                            ReferenceProxy referenceProxy = subSlot.AttachComponent<ReferenceProxy>();
                            assetProxy.AssetReference.Target = animProvider;
                            referenceProxy.Reference.Target = animProvider;
                            animator.Fields.Add().Target = text.Value;

                            await default(ToBackground);

                            if (!Config.GetValue(keepSubFiles))
                            {
                                if (File.Exists(outputPathSrt))
                                    File.Delete(outputPathSrt);
                            }
                            i++;
                        }
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
