using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xabe.FFmpeg;

namespace ResoniteSubtitleImporter
{
    public class ImportHelper
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

        public static void CleanSrt(string file, string targetFile)
        {
            if (!File.Exists(file))
                return;

            string content = null;
            using (var fs = File.OpenRead(file))
            {
                using (var sr = new StreamReader(fs))
                {
                    content = sr.ReadToEnd();
                }
            }

            if (File.Exists(targetFile))
                File.Delete(targetFile);

            using (var sr = new StringReader(content))
            {
                using (var writer = new StreamWriter(File.OpenWrite(targetFile)))
                {
                    var fontStartRegex = new Regex("<\\s*font[^>]*>");
                    var fontEndRegex = new Regex("</\\s*font\\s*>");

                    string line = null;
                    while ((line = sr.ReadLine()) != null)
                    {
                        Match match = null;
                        // remove all font tags as they don't work with the RichText parser
                        while ((match = fontStartRegex.Match(line)).Success)
                        {
                            line = line.Remove(match.Index, match.Length);
                        }
                        while ((match = fontEndRegex.Match(line)).Success)
                        {
                            line = line.Remove(match.Index, match.Length);
                        }

                        writer.WriteLine(line);
                    }
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
            Slot subRootSlot = world.LocalUserSpace.AddSlot(SubtitleRootSlotNamePrefix + filename);
            await default(ToBackground);

            ResoniteSubtitleImporter.Msg($"Importing {info.SubtitleStreams.Count()} subtitles");
            var i = 0;
            foreach (var subtitle in info.SubtitleStreams)
            {
                var subname = i + " - " + (string.IsNullOrEmpty(subtitle.Title) ? subtitle.Language : subtitle.Title);
                // sanitize filename
                foreach (var invalid in Path.GetInvalidFileNameChars())
                {
                    subname = subname.Replace(invalid, ' ');
                }

                var nameSrt = String.Format("{0}-{1}.srt",
                    filename, subname);
                var outputPathSrt = Path.Combine(Path.GetDirectoryName(path), nameSrt);
                var outputPathCleaned = Path.Combine(Path.GetDirectoryName(path), "cleaned_" + nameSrt);

                IConversionResult result;
                try
                {
                    result = await FFmpeg.Conversions.New()
                        .AddStream(subtitle)
                        .SetOutput(outputPathSrt)
                        .SetOverwriteOutput(true)
                        .Start();
                }
                catch (Exception e)
                {
                    ResoniteSubtitleImporter.Error("Subtitle extraction failed!");
                    ResoniteSubtitleImporter.Error(e);
                    if (File.Exists(outputPathSrt))
                    {
                        try
                        {
                            File.Delete(outputPathSrt);
                        }
                        catch { }
                    }
                    continue;
                }

                var outputFile = outputPathSrt;

                try
                {
                    ResoniteSubtitleImporter.Msg("Try to clean subtitles");
                    CleanSrt(outputPathSrt, outputPathCleaned);
                    outputFile = outputPathCleaned;
                    ResoniteSubtitleImporter.Msg("Subtitles cleaned successfully");
                }
                catch (Exception ex)
                {
                    ResoniteSubtitleImporter.Error("Cleaning subtitles failed");
                    ResoniteSubtitleImporter.Error(ex);
                }

                ResoniteSubtitleImporter.Msg($"SRT Subtitle conversion took {(result.EndTime - result.StartTime).TotalSeconds} seconds");


                // import sub
                ResoniteSubtitleImporter.Msg("importing subtitle " + subname);
                await default(ToBackground);
                AnimX anim = SubtitleImporter.Import(outputFile);
                Uri subUri = await world.Engine.LocalDB.SaveAssetAsync(anim);
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
                    if (File.Exists(outputPathCleaned))
                        File.Delete(outputPathCleaned);
                }
                i++;
            }

            // reparent slot to parent slot
            await default(ToWorld);
            subRootSlot.Tag = SubtitleRootTag;
            subRootSlot.SetParent(parent, false);
            subRootSlot.SetIdentityTransform();

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
