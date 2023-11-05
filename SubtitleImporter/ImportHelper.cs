using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using SubtitlesParser.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Streams.SubtitleStream;

namespace ResoniteSubtitleImporter
{
    public class ImportHelper
    {
        public static string SubtitleRootTag = "SubtitleImport";
        public static string SubtitleRootSlotNamePrefix = "Subtitles - ";

        public static Dictionary<string, SubtitlesFormat> SupportedSubtitleCodecs = new Dictionary<string, SubtitlesFormat>
            {
                { SubtitleCodec.ass.ToString(), SubtitlesFormat.SubStationAlphaFormat },
                { SubtitleCodec.srt.ToString(), SubtitlesFormat.SubRipFormat },
                { SubtitleCodec.microdvd.ToString(), SubtitlesFormat.MicroDvdFormat },
                { SubtitleCodec.webvtt.ToString(), SubtitlesFormat.WebVttFormat },
                { SubtitleCodec.subviewer.ToString(), SubtitlesFormat.SubViewerFormat },
                { SubtitleCodec.subviewer1.ToString(), SubtitlesFormat.SubViewerFormat },
            };

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

        public static void CleanSSA(string file, string targetFile)
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
                    var assTagsRegex = new Regex("\\{\\\\[^\\}]+\\}");

                    string line = null;
                    while ((line = sr.ReadLine()) != null)
                    {
                        // remove all font tags as they don't work with the RichText parser
                        line = assTagsRegex.Replace(line, "");

                        writer.WriteLine(line);
                    }
                }
            }
        }

        public static void CleanSRT(string file, string targetFile)
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
                        line = fontStartRegex.Replace(line, "");
                        line = fontEndRegex.Replace(line, "");

                        writer.WriteLine(line);
                    }
                }
            }
        }

        public static async Task<Slot> ImportSubtitles(string path, Slot parent, World world, bool keepSubFiles)
        {
            if (!world.CanSpawnObjects())
            {
                ResoniteSubtitleImporter.Msg("No spawn perms, cannot import subtitles");
                return null;
            }
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

                var extension = "srt";
                if (SupportedSubtitleCodecs.TryGetValue(subtitle.Codec, out var codec))
                {
                    ResoniteSubtitleImporter.Msg($"Found supported subtitle codec {subtitle.Codec}");
                    extension = codec.Extension.Split('.')[1];
                }

                var nameExtract = String.Format("{0}-{1}.{2}",
                    filename, subname, extension);
                var outputPathExtract = Path.Combine(Path.GetDirectoryName(path), nameExtract);
                var outputPathCleaned = Path.Combine(Path.GetDirectoryName(path), "cleaned_" + nameExtract);

                IConversionResult result;
                try
                {
                    result = await FFmpeg.Conversions.New()
                        .AddStream(subtitle)
                        .SetOutput(outputPathExtract)
                        .SetOverwriteOutput(true)
                        .Start();
                }
                catch (Exception e)
                {
                    ResoniteSubtitleImporter.Error("Subtitle extraction failed!");
                    ResoniteSubtitleImporter.Error(e);
                    if (File.Exists(outputPathExtract))
                    {
                        try
                        {
                            File.Delete(outputPathExtract);
                        }
                        catch { }
                    }
                    continue;
                }

                var outputFile = outputPathExtract;

                if (extension == "srt")
                {
                    try
                    {
                        ResoniteSubtitleImporter.Msg("Try to clean subtitles");
                        CleanSRT(outputPathExtract, outputPathCleaned);
                        outputFile = outputPathCleaned;
                        ResoniteSubtitleImporter.Msg("Subtitles cleaned successfully");
                    }
                    catch (Exception ex)
                    {
                        ResoniteSubtitleImporter.Error("Cleaning subtitles failed");
                        ResoniteSubtitleImporter.Error(ex);
                        outputFile = outputPathExtract;
                    }
                }
                else if (extension == "ssa")
                {
                    try
                    {
                        ResoniteSubtitleImporter.Msg("Try to clean subtitles");
                        CleanSSA(outputPathExtract, outputPathCleaned);
                        outputFile = outputPathCleaned;
                        ResoniteSubtitleImporter.Msg("Subtitles cleaned successfully");
                    }
                    catch (Exception ex)
                    {
                        ResoniteSubtitleImporter.Error("Cleaning subtitles failed");
                        ResoniteSubtitleImporter.Error(ex);
                        outputFile = outputPathExtract;
                    }
                }

                ResoniteSubtitleImporter.Msg($"SRT Subtitle conversion took {(result.EndTime - result.StartTime).TotalSeconds} seconds");


                // import sub
                ResoniteSubtitleImporter.Msg("importing subtitle " + subname);
                await default(ToBackground);
                AnimX anim = ImportSubtitle(outputFile);
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
                    if (File.Exists(outputPathExtract))
                        File.Delete(outputPathExtract);
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

        /// <summary>
        /// Imports the given subtitle file into an AnimX.
        /// Rewrite of SubtitleImporter.Import() as that does not check the sub type beforehand
        /// </summary>
        /// <param name="path">Path to the sub file</param>
        /// <returns>The AnimX of the subtitle</returns>
        public static AnimX ImportSubtitle(string path)
        {
            var newlineRegex = new Regex("\\\\N"); // ASS has \N as newlines for some reason
            var subParser = new SubtitlesParser.Classes.Parsers.SubParser();
            var format = subParser.GetMostLikelyFormat(path);
            ResoniteSubtitleImporter.Debug($"Found likely sub format {format?.ToString()}");

            List<SubtitleItem> items;
            using (var stream = File.OpenRead(path))
            {
                items = subParser.ParseStream(stream, Encoding.UTF8, format);
            }
            var anim = new AnimX(float.MaxValue, Path.GetFileName(path));
            anim.GlobalDuration = (float)items.GetLast().EndTime * 0.001f;
            var track = anim.AddTrack<DiscreteStringAnimationTrack>();
            track.Node = "Subtitle";
            track.Property = "Text";
            int prevEndTime = items[0].StartTime;
            if (prevEndTime != 0)
                track.InsertKeyFrame(null, 0f);

            foreach (var item in items)
            {
                // end previous item if there is a pause
                if (item.StartTime > prevEndTime)
                    track.InsertKeyFrame(null, prevEndTime * 0.001f);

                var text = string.Join("\n", item.Lines);
                text = newlineRegex.Replace(text, "\n"); // fix \N newlines
                track.InsertKeyFrame(text, (float)item.StartTime * 0.001f);
                prevEndTime = item.EndTime;
            }
            // finish last item
            track.InsertKeyFrame(null, prevEndTime * 0.001f);

            return anim;
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
