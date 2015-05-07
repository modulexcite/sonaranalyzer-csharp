﻿/*
 * SonarQube C# Code Analysis
 * Copyright (C) 2015 SonarSource
 * dev@sonar.codehaus.org
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SonarQube.CSharp.CodeAnalysis.Descriptor;
using SonarQube.CSharp.CodeAnalysis.Rules;
using SonarQube.CSharp.CodeAnalysis.Runner;
using SonarQube.CSharp.CodeAnalysis.SonarQube.Settings;

namespace SonarQube.CSharp.CodeAnalysis.IntegrationTest
{
    [TestClass]
    public class ItSources
    {
        private FileInfo[] codeFiles;
        private IList<Type> analyzerTypes;
        private string xmlInputPattern;
        private static readonly XmlWriterSettings Settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "  "
        };
        private static readonly XmlWriterSettings FragmentSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "  ",
            ConformanceLevel = ConformanceLevel.Fragment,
            CloseOutput = false,
            OmitXmlDeclaration = true
        };

        [TestInitialize]
        public void Setup()
        {
            var pathToRoot = Environment.GetEnvironmentVariable(
                ConfigurationManager.AppSettings["env.var.it-sources"]);

            var rootDirectory = new DirectoryInfo(
                Path.Combine(pathToRoot, ConfigurationManager.AppSettings["path.it-sources.input"]));

            codeFiles = rootDirectory.GetFiles("*.cs", SearchOption.AllDirectories);

            analyzerTypes = new RuleFinder().GetAllAnalyzerTypes().ToList();

            xmlInputPattern = GenerateAnalysisInputFilePattern();
        }

        private string GenerateAnalysisInputFilePattern()
        {
            var memoryStream = new MemoryStream();
            using (var writer = XmlWriter.Create(memoryStream, Settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("AnalysisInput");

                //some mandatory settings
                writer.WriteStartElement("Settings");
                writer.WriteStartElement("Setting");
                writer.WriteStartElement("Key");
                writer.WriteString("sonar.cs.ignoreHeaderComments");
                writer.WriteEndElement();
                writer.WriteStartElement("Value");
                writer.WriteString("true");
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
                
                writer.WriteStartElement("Files");

                foreach (var codeFile in codeFiles)
                {
                    writer.WriteStartElement("File");
                    writer.WriteString(codeFile.FullName);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        private static string GenerateAnalysisInputFileSegment(Type analyzerType)
        {
            var builder = new StringBuilder();
            using (var writer = XmlWriter.Create(builder, FragmentSettings))
            {
                writer.WriteStartElement("Rule");
                writer.WriteStartElement("Key");
                var rule = analyzerType.GetCustomAttribute<RuleAttribute>();
                writer.WriteString(rule.Key);
                writer.WriteEndElement();


                switch (rule.Key)
                {
                    case CommentRegularExpression.DiagnosticId:
                        writer.WriteStartElement("Parameters");
                        {
                            writer.WriteStartElement("Parameter");
                            writer.WriteStartElement("Key");
                            writer.WriteString("RuleKey");
                            writer.WriteEndElement();
                            writer.WriteStartElement("Value");
                            writer.WriteString("S124-1");
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                        }
                        {
                            writer.WriteStartElement("Parameter");
                            writer.WriteStartElement("Key");
                            writer.WriteString("message");
                            writer.WriteEndElement();
                            writer.WriteStartElement("Value");
                            writer.WriteString("Some message");
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                        }
                        {
                            writer.WriteStartElement("Parameter");
                            writer.WriteStartElement("Key");
                            writer.WriteString("regularExpression");
                            writer.WriteEndElement();
                            writer.WriteStartElement("Value");
                            writer.WriteString("(?i)TODO");
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                        break;
                    default:
                        var parameters = analyzerType.GetProperties()
                    .Where(p => p.GetCustomAttributes<RuleParameterAttribute>().Any())
                    .ToList();

                        if (parameters.Any())
                        {
                            writer.WriteStartElement("Parameters");

                            foreach (var propertyInfo in parameters)
                            {
                                var ruleParameter = propertyInfo.GetCustomAttribute<RuleParameterAttribute>();

                                writer.WriteStartElement("Parameter");
                                writer.WriteStartElement("Key");
                                writer.WriteString(ruleParameter.Key);
                                writer.WriteEndElement();
                                writer.WriteStartElement("Value");
                                writer.WriteString(ruleParameter.DefaultValue);
                                writer.WriteEndElement();
                                writer.WriteEndElement();
                            }

                            writer.WriteEndElement();
                        }
                        break;
                }

                writer.WriteEndElement();
            }

            return builder.ToString();
        }

        private string GenerateAnalysisInputFile(Type analyzerType)
        {
            var xdoc = new XmlDocument();
            xdoc.LoadXml(xmlInputPattern);

            var rules = xdoc.CreateElement("Rules");
            rules.InnerXml = GenerateAnalysisInputFileSegment(analyzerType);

            xdoc.DocumentElement.AppendChild(rules);

            return xdoc.OuterXml;
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void ItSources_Match_Expected()
        {
            var sw = Stopwatch.StartNew();

            var output = new DirectoryInfo("GeneratedOutput");
            if (!output.Exists)
            {
                output.Create();
            }

            foreach (var analyzerType in analyzerTypes)
            {
                var ruleId = analyzerType.GetCustomAttribute<RuleAttribute>().Key;

                var input = GenerateAnalysisInputFile(analyzerType);
                var tempInputFilePath = Path.GetTempFileName();
                try
                {
                    File.AppendAllText(tempInputFilePath, input);

                    var retValue = Program.Main(new[]
                    {
                        tempInputFilePath,
                        Path.Combine(output.FullName, string.Format("{0}.xml", ruleId))
                    });

                    if (retValue != 0)
                    {
                        throw new Exception("Analysis failed with error");
                    }
                }
                finally
                {
                    File.Delete(tempInputFilePath);
                }
                Console.WriteLine(sw.Elapsed);
            }

            sw.Stop();
            Console.WriteLine(sw.Elapsed);
        }
    }
}
