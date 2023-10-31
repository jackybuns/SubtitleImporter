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

        private static async Task<Slot> ImportSubtitles(VideoTextureProvider provider, bool enforceObjectRoot)
        {
            var uri = provider.URL?.Value;
            if (uri == null)
                return null;
            if (AssetHelper.IsStreamingProtocol(uri))
                return null;
            if (AssetHelper.IsVideoStreamingService(uri))
                return null;

            await default(ToWorld);
            var root = provider.Slot.GetObjectRoot();
            // ensure that we import to the video player on an automatic import
            if (enforceObjectRoot && (root == null || root == provider.World.RootSlot || root == provider.LocalUserSpace))
            {
                ResoniteSubtitleImporter.Msg("Could not find root of video player to import subtitles to. Make sure the player has an ObjectRoot component.");
                return null;
            }

            // get the asset file so we can convert it
            await default(ToBackground);
            GatherResult gatherResult = await provider.Asset.AssetManager.GatherAsset(uri, 0f, DB_Endpoint.Video).ConfigureAwait(continueOnCapturedContext: false);
            string file = await gatherResult.GetFile().ConfigureAwait(continueOnCapturedContext: false);
            if (File.Exists(file))
            {
                return await ImportHelper.ImportSubtitles(file, root, provider.World, false);
            }

            return null;
        }

        //private void VideoLoaded(VideoTexture texture, bool assetInstanceChanged)
        [HarmonyPostfix]
        [HarmonyPatch("VideoLoaded")]
        public static void VideoLoadedPostFix(VideoTextureProvider __instance, bool assetInstanceChanged)
        {
            if (!ResoniteSubtitleImporter.Config.GetValue(ResoniteSubtitleImporter.enabled))
                return;

            var allocator = ImportHelper.GetAllocatingUser(__instance.Slot);
            if (!assetInstanceChanged)
                return;

            if (__instance.Stream.Value == true)
                return;

            // if the local user spawned the video texture or was the last to change the URL perform auto import
            if ((__instance.URL != null && __instance.URL.LastModifyingUser == __instance.LocalUser) ||
                (allocator != null && allocator == __instance.LocalUser))
            {
                ResoniteSubtitleImporter.Msg("Automatically importing subtitles");
                __instance.StartGlobalTask(async delegate
                {
                    try
                    {
                        await ImportSubtitles(__instance, true);
                    }
                    catch (Exception ex)
                    {
                        ResoniteSubtitleImporter.Error("Error on auto subtitle import");
                        ResoniteSubtitleImporter.Error(ex);
                    }
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
                ResoniteSubtitleImporter.Msg("Manually importing subtitles from VideoTextureProvider");

                if (__instance.Stream.Value == true)
                {
                    ResoniteSubtitleImporter.Msg("Video player is set to stream, cannot import subtitles");
                    return;
                }

                var uri = __instance.URL.Value;
                if (uri == null)
                    return;

                button.Enabled = false;
                button.LabelText = "Importing...";
                if (__instance.Asset.LoadState == AssetLoadState.FullyLoaded)
                {
                    ResoniteSubtitleImporter.Msg("Importing subtitles from VideoTextureProvider");
                    __instance.StartGlobalTask(async delegate
                    {
                        try
                        {
                            var subRootSlot = await ImportSubtitles(__instance, false);
                            await default(ToWorld);
                            button.LabelText = "Done";
                            await default(ToBackground);
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            await default(ToWorld);
                            button.LabelText = "Import Subtitles";
                            button.Enabled = true;

                            if (subRootSlot != null && ResoniteSubtitleImporter.Config.GetValue(ResoniteSubtitleImporter.openInspector))
                            {
                                await default(ToWorld);
                                DevCreateNewForm.OpenInspector(subRootSlot);
                            }
                        }
                        catch (Exception ex)
                        {
                            ResoniteSubtitleImporter.Error(ex);
                            await default(ToWorld);
                            button.LabelText = "Error!";
                            await default(ToBackground);
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            await default(ToWorld);
                            button.LabelText = "Import Subtitles";
                            button.Enabled = true;
                        }
                    });
                }

            };
        }
    }
}
