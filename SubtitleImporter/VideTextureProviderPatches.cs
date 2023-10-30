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
    [HarmonyPatch(typeof(VideoTextureProvider))]
    internal class VideTextureProviderPatches
    {

        private static async Task ImportSubtitles(VideoTextureProvider provider)
        {
            var uri = provider.URL.Value;
            await default(ToWorld);
            var root = provider.Slot.GetObjectRoot();
            await default(ToBackground);
            GatherResult gatherResult = await provider.Asset.AssetManager.GatherAsset(uri, 0f, DB_Endpoint.Video).ConfigureAwait(continueOnCapturedContext: false);
            string file = await gatherResult.GetFile().ConfigureAwait(continueOnCapturedContext: false);
            if (File.Exists(file))
            {
                await ImportHelper.ImportSubtitles(file, root, provider.World, false);
            }
        }

        //private void VideoLoaded(VideoTexture texture, bool assetInstanceChanged)
        [HarmonyPostfix]
        [HarmonyPatch("VideoLoaded")]
        public static void VideoLoadedPostFix(VideoTextureProvider __instance, bool assetInstanceChanged)
        {
            var allocator = ImportHelper.GetAllocatingUser(__instance.Slot);
            if (!assetInstanceChanged)
                return;

            // if the local user spawned the video texture or was the last to change the URL perform auto import
            if ((__instance.URL != null && __instance.URL.LastModifyingUser == __instance.LocalUser) ||
                (allocator != null && allocator == __instance.LocalUser))
            {
                ResoniteSubtitleImporter.Msg("Automatically importing subtitles");
                __instance.StartGlobalTask(async delegate
                {
                    await ImportSubtitles(__instance);
                });
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(VideoTextureProvider.BuildInspectorUI))]
        public static void InpsectorPostfix(VideoTextureProvider __instance, UIBuilder ui)
        {
            var button = ui.Button("Import Subtitles");
            button.LocalPressed += async (sender, args) =>
            {
                button.Enabled = false;
                button.LabelText = "Importing...";
                ResoniteSubtitleImporter.Msg("Manually importing subtitles from VideoTextureProvider");

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
                    ResoniteSubtitleImporter.Msg("Importing subtitles from VideoTextureProvider");
                    __instance.StartGlobalTask(async delegate
                    {
                        await ImportSubtitles(__instance);
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

        private static void URL_OnValueChange(SyncField<Uri> syncField)
        {
            throw new NotImplementedException();
        }
    }
}
