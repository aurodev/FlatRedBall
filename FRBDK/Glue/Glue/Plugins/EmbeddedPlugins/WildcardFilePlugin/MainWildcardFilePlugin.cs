﻿using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.IO;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;

namespace FlatRedBall.Glue.Plugins.EmbeddedPlugins.WildcardFilePlugin
{
    [Export(typeof(PluginBase))]
    internal class MainWildcardFilePlugin : EmbeddedPlugin
    {
        public override void StartUp()
        {
            this.ReactToFileChange += HandleFileChanged;
        }

        private void HandleFileChanged(FilePath filePath, FileChangeType fileChangeType)
        {
            var project = GlueState.Self.CurrentGlueProject;

            var exists = filePath.Exists();

            if(exists)
            {
                // was it added?
                foreach(var wildcardFile in project.GlobalFileWildcards)
                {
                    if(IsFileRelativeToWildcard(filePath, wildcardFile))
                    {
                        // clone it, add it here
                        var clone = wildcardFile.Clone();
                        clone.Name = filePath.RelativeTo(GlueState.Self.ContentDirectory);
                        clone.IsCreatedByWildcard = true;
                        GlueCommands.Self.GluxCommands.AddReferencedFileToGlobalContent(clone);
                        break;
                    }
                }
            }
            else
            {
                // was it removed?
                var wildcardGlobalFiles = project.GlobalFiles.Where(item => item.IsCreatedByWildcard).ToList();
                foreach (var file in wildcardGlobalFiles)
                {
                    var fileForCandidate = GlueCommands.Self.GetAbsoluteFilePath(file);

                    if(fileForCandidate == filePath)
                    {
                        GlueCommands.Self.GluxCommands.RemoveReferencedFile(file, null);
                    }
                }
            }
        }

        private bool IsFileRelativeToWildcard(FilePath changedFilePath, SaveClasses.ReferencedFileSave wildcardFile)
        {
            var changedFileName = changedFilePath.FullPath;

            // This could be faster, but we'll cheat and use some (probably slow) operations:
            var wildcardFilePath = GlueCommands.Self.GetAbsoluteFilePath(wildcardFile);
            FilePath directoryWithNoWildcard = wildcardFilePath;
            while (directoryWithNoWildcard.FullPath.Contains("*"))
            {
                directoryWithNoWildcard = directoryWithNoWildcard.GetDirectoryContainingThis();
            }

            var suffix = wildcardFilePath.RelativeTo(directoryWithNoWildcard);

            if(suffix.StartsWith("**"))
            {
                // we're going to any depth
                if(suffix == "**")
                {
                    // as long as the file is relative to the wildcard path, then return true
                    return directoryWithNoWildcard.IsRootOf(changedFileName);
                }
                else if(suffix.Contains('/'))
                {
                    var suffixFilePattern = wildcardFilePath.NoPath;

                    var allFiles = System.IO.Directory
                        .GetFiles(directoryWithNoWildcard.FullPath, suffixFilePattern, System.IO.SearchOption.AllDirectories)
                        .Select(item => new FilePath(item));

                    return allFiles.Contains(changedFilePath);
                }
                else
                {
                    // unsupported pattern
                    return false;
                }
            }
            else
            {
                // we're only looking in the current folder:
                var suffixFilePattern = wildcardFilePath.NoPath;

                var allFiles = System.IO.Directory
                    .GetFiles(directoryWithNoWildcard.FullPath, suffixFilePattern, System.IO.SearchOption.TopDirectoryOnly)
                    .Select(item => new FilePath(item));
                return allFiles.Contains(changedFilePath);
            }
        }
    }
}
