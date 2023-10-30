using Elements.Assets;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using SkyFrost.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ResoniteSubtitleImporter
{
    [HarmonyPatch(typeof(VideoTextureProvider), nameof(VideoTextureProvider.BuildInspectorUI))]
    internal class VideTextureProviderPatches
    {
        public static void Postfix(VideoTextureProvider __instance, UIBuilder ui)
        {
            var button = ui.Button("Import Subtitles");
            button.LocalPressed += async (sender, args) =>
            {
                button.Enabled = false;
                button.LabelText = "Importing...";
                if (__instance.Stream.Value == true)
                {
                    ResoniteSubtitleImporter.Msg("Video player is set to stream, cannot import subtitles");
                    return;
                }

                var uri = __instance.URL.Value;
                if (uri == null)
                    return;

                if (__instance.Asset.LoadState == AssetLoadState.FullyLoaded)
                {
                    __instance.StartGlobalTask(async delegate
                    {
                        ResoniteSubtitleImporter.Msg("Importing subtitles from VideoTextureProvider");
                        await default(ToWorld);
                        var root = __instance.Slot.GetObjectRoot();
                        await default(ToBackground);
                        GatherResult gatherResult = await __instance.Asset.AssetManager.GatherAsset(uri, 0f, DB_Endpoint.Video).ConfigureAwait(continueOnCapturedContext: false);
                        string file = await gatherResult.GetFile().ConfigureAwait(continueOnCapturedContext: false);
                        ResoniteSubtitleImporter.Msg(file);
                        if (File.Exists(file))
                        {
                            var subsRootSlot = await ImportHelper.ImportSubtitles(file, root, __instance.World, false);
                        }

                        await default(ToWorld);
                        button.LabelText = "Done";
                        await default(ToBackground);
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        await default(ToWorld);
                        button.LabelText = "Import Subtitles";
                        button.Enabled = true;
                    });
                }

            };
        }
    }
}
