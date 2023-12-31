﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using SubtitleImporter;
using System;
using System.IO;

namespace SubtitleImporterTests
{
    [TestClass]
    public class SubCleanTest
    {
        [TestMethod]
        public void TestCleanSrt()
        {
            var input = "Testfiles/testsub.srt";
            var output = "out.srt";
            var compare = "Testfiles/out.srt";
            ImportHelper.CleanSRT(input, output);

            using (var reader = new StreamReader(File.OpenRead(output)))
            {
                using (var compareReader = new StreamReader(File.OpenRead(compare)))
                {
                    var content = reader.ReadToEnd();
                    var comparecontent = compareReader.ReadToEnd();
                    Console.WriteLine(content);
                    Assert.AreEqual(comparecontent, content);
                }
            }

            // cleanup
            if (File.Exists(output))
                File.Delete(output);
        }

        [TestMethod]
        public void CleanSameFile()
        {
            var input = "Testfiles/testsub.srt";
            var output = "out.srt";

            File.Copy(input, output, true);

            ImportHelper.CleanSRT(output, output); // should not crash

            // cleanup
            if (File.Exists(output))
                File.Delete(output);
        }
    }
}
