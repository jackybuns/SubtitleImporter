﻿using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace ResoniteSubtitleImporter
{
    internal class ImportHelper
    {
        public static string SubtitleRootTag = "SubtitleImport";
        public static string SubtitleRootSlotNamePrefix = "Subtitles - ";

        /// <summary>
        /// Destroys all child slots with the <see cref="SubtitleRootTag"/> tag and which name starts with <see cref="SubtitleRootSlotNamePrefix"/>.
        /// Only destroys them if the provided slot is not the world root or the LocalUserRoot for safety.
        /// </summary>
        /// <param name="slot">The slot which children should be cleaned</param>
        public static void CleanupSubtitles(Slot slot)
        {
            if (slot != null && slot != slot.World.RootSlot && slot != slot.LocalUserSpace)
            {
                var oldSubs = slot.GetChildrenWithTag(SubtitleRootTag);
                for (int i = oldSubs.Count - 1; i >= 0; i--)
                {
                    if (oldSubs[i].Name.StartsWith(SubtitleRootSlotNamePrefix))
                        oldSubs[i].Destroy();
                }
            }
        }

        public static async Task<Slot> ImportSubtitles(string path, Slot parent, World world, bool keepSubFiles)
        {
            var filename = Path.GetFileNameWithoutExtension(path);
            ResoniteSubtitleImporter.Msg("Importing subtitles");

            await default(ToBackground);
            var info = await FFmpeg.GetMediaInfo(path);
            if (!info.SubtitleStreams.Any())
                return null;
            await default(ToWorld);
            CleanupSubtitles(parent);
            Slot subRootSlot = parent.AddSlot(SubtitleRootSlotNamePrefix + filename);
            subRootSlot.Tag = SubtitleRootTag;
            await default(ToBackground);

            ResoniteSubtitleImporter.Msg($"Importing {info.SubtitleStreams.Count()} subtitles");
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

                ResoniteSubtitleImporter.Msg($"VTT Subtitle conversion took {(result.EndTime - result.StartTime).TotalSeconds} seconds");

                var vttinfo = await FFmpeg.GetMediaInfo(outputPathVtt);
                result = await FFmpeg.Conversions.New()
                    .AddStream(vttinfo.SubtitleStreams.First())
                    .SetOutput(outputPathSrt)
                    .SetOverwriteOutput(true)
                    .Start();

                ResoniteSubtitleImporter.Msg($"SRT Subtitle conversion took {(result.EndTime - result.StartTime).TotalSeconds} seconds");

                // cleanup vtt file
                if (File.Exists(outputPathVtt))
                    File.Delete(outputPathVtt);


                // import sub
                ResoniteSubtitleImporter.Msg("importing subtitle " + subname);
                await default(ToBackground);
                AnimX anim = SubtitleImporter.Import(outputPathSrt);
                if (anim == null)
                {
                    ResoniteSubtitleImporter.Error("imported subtitle animation is null");
                    continue;
                }
                Uri subUri = await world.Engine.LocalDB.SaveAssetAsync(anim);
                if (subUri == null)
                {
                    ResoniteSubtitleImporter.Error("subtitle animation asset uri is null");
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
                copy.Source.Target = animProvider.URL;
                copy.Target.Target = dynUri.Value;

                var text = subSlot.AttachComponent<DynamicValueVariable<string>>();
                text.VariableName.Value = "Subtitle/Text";
                AssetProxy<FrooxEngine.Animation> assetProxy = subSlot.AttachComponent<AssetProxy<FrooxEngine.Animation>>();
                ReferenceProxy referenceProxy = subSlot.AttachComponent<ReferenceProxy>();
                assetProxy.AssetReference.Target = animProvider;
                referenceProxy.Reference.Target = animProvider;
                animator.Fields.Add().Target = text.Value;

                var dynAnim = subSlot.AttachComponent<DynamicReferenceVariable<IAssetProvider<FrooxEngine.Animation>>>();
                dynAnim.VariableName.Value = "Subtitle/Animation";
                dynAnim.Reference.TrySet(animProvider);


                await default(ToBackground);

                if (!keepSubFiles)
                {
                    if (File.Exists(outputPathSrt))
                        File.Delete(outputPathSrt);
                }
                i++;
            }

            return subRootSlot;
        }

        public static FrooxEngine.User GetAllocatingUser(Slot slot)
        {
            slot.ReferenceID.ExtractIDs(out var position, out var userAllocId);
            var user = slot.World.GetUserByAllocationID(userAllocId);
            if (user == null)
                return null;
            if (position < user.AllocationIDStart)
                return null;
            return user;
        }
    }
}
