using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using HarmonyLib; // HarmonyLib comes included with a ResoniteModLoader install
using ResoniteModLoader;
using System;
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
        class VideoImporterDialogPatch
        {
            static void Postfix(Slot slot, string path)
            {
                if (!Config.GetValue(enabled))
                    return;

                if (slot == null)
                    Error("Slot is null");

                slot.World.Coroutines.StartTask(async () =>
                {
                    Msg("Importing subtitles");
                    await default(ToWorld);
                    //var subRootSlot = slot.AddSlot("Subtitles");
                    var subRootSlot = slot.World.LocalUserSpace.AddSlot("Subtitles");
                    await default(ToBackground);
                    Uri uri = new Uri(path);
                    if (uri.Scheme == "file")
                    {
                        var info = await FFmpeg.GetMediaInfo(path);

                        foreach (var subtitle in info.SubtitleStreams)
                        {
                            var nameVtt = Path.GetFileName(path) + "-" + subtitle.Language + ".vtt";
                            var nameSrt = Path.GetFileName(path) + "-" + subtitle.Language + ".srt";
                            var outputPathVtt = Path.Combine(Path.GetDirectoryName(path), nameVtt);
                            var outputPathSrt = Path.Combine(Path.GetDirectoryName(path), nameSrt);

                            await FFmpeg.Conversions.New()
                                .AddStream(subtitle)
                                .SetOutput(outputPathSrt)
                                .SetOverwriteOutput(true)
                                .Start();


                            // import sub
                            try
                            {

                                Msg("importing subtitle " + subtitle.Language);
                                await default(ToBackground);
                                Msg("Before importing anim");
                                AnimX anim = SubtitleImporter.Import(outputPathSrt);
                                if (anim == null)
                                {
                                    Error("imported subtitle animation is null");
                                    continue;
                                }
                                Msg("before saving asset");
                                if (slot == null)
                                    Error("inner Slot is null");
                                Uri subUri = await slot.World.Engine.LocalDB.SaveAssetAsync(anim);
                                if (subUri == null)
                                {
                                    Error("subtitle animation asset uri is null");
                                    continue;
                                }
                                Msg("after saving asset");
                                await default(ToWorld);
                                var subSlot = subRootSlot.AddSlot(subtitle.Language);
                                var animProvider = subSlot.AttachComponent<StaticAnimationProvider>();
                                animProvider.URL.Value = subUri;
                                var animator = subSlot.AttachComponent<Animator>();
                                animator.Clip.Target = animProvider;
                            }
                            catch (Exception ex)
                            {
                                Error(ex.Message);
                                Error(ex.StackTrace);
                            }
                            /*
                            var text = subSlot.AttachComponent<ValueField<string>>();
                            AssetProxy<FrooxEngine.Animation> assetProxy = subSlot.AttachComponent<AssetProxy<FrooxEngine.Animation>>();
                            ReferenceProxy referenceProxy = subSlot.AttachComponent<ReferenceProxy>();
                            assetProxy.AssetReference.Target = animProvider;
                            referenceProxy.Reference.Target = animProvider;
                            animator.Fields.Add().Target = text.Value;
                            */
                            /*
                            var dynVarSpace = subSlot.AttachComponent<DynamicVariableSpace>();
                            var dynVar = subSlot.AttachComponent<DynamicReference<Animator>>();
                            */
                            await default(ToBackground);

                            /*
                            // first convert to vtt to get rid of nasty html tags we don't need
                            await FFmpeg.Conversions.New()
                                .AddStream(subtitle)
                                .SetOutput(outputPathVtt)
                                .Start();

                            // then convert that to srt as resonite does not support vtt
                            var vttInfo = await FFmpeg.GetMediaInfo(outputPathVtt);
                            await FFmpeg.Conversions.New()
                                .AddStream(vttInfo.SubtitleStreams.First())
                                .SetOutput(outputPathSrt)
                                .Start();
                            */

                        }

                    }
                    await default(ToWorld);
                });
            }

        }

    }
}
