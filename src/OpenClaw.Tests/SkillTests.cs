using OpenClaw.Core.Skills;
using Xunit;

namespace OpenClaw.Tests;

public class SkillLoaderTests
{
    [Fact]
    public void ParseSkillContent_ValidFrontmatter_ReturnsSkill()
    {
        var content = """
            ---
            name: test-skill
            description: A test skill for unit testing
            ---
            Use the test tool to run tests.
            Always validate output before returning.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/test-skill", SkillSource.Workspace);

        Assert.NotNull(skill);
        Assert.Equal("test-skill", skill!.Name);
        Assert.Equal("A test skill for unit testing", skill.Description);
        Assert.Contains("test tool", skill.Instructions);
        Assert.Equal("/skills/test-skill", skill.Location);
        Assert.Equal(SkillSource.Workspace, skill.Source);
    }

    [Fact]
    public void ParseSkillContent_MissingFrontmatter_ReturnsNull()
    {
        var content = "Just some markdown without frontmatter.";

        var skill = SkillLoader.ParseSkillContent(content, "/skills/bad", SkillSource.Workspace);

        Assert.Null(skill);
    }

    [Fact]
    public void ParseSkillContent_MissingName_ReturnsNull()
    {
        var content = """
            ---
            description: No name here
            ---
            Instructions body.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/noname", SkillSource.Workspace);

        Assert.Null(skill);
    }

    [Fact]
    public void ParseSkillContent_WithMetadata_ParsesRequirements()
    {
        var content = """
            ---
            name: gemini-skill
            description: Use Gemini for coding
            metadata: {"openclaw": {"requires": {"bins": ["gemini"], "env": ["GEMINI_API_KEY"]}, "primaryEnv": "GEMINI_API_KEY", "emoji": "♊️"}}
            ---
            Use the gemini CLI tool.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/gemini", SkillSource.Managed);

        Assert.NotNull(skill);
        Assert.Equal("gemini-skill", skill!.Name);
        Assert.Single(skill.Metadata.RequireBins);
        Assert.Equal("gemini", skill.Metadata.RequireBins[0]);
        Assert.Single(skill.Metadata.RequireEnv);
        Assert.Equal("GEMINI_API_KEY", skill.Metadata.RequireEnv[0]);
        Assert.Equal("GEMINI_API_KEY", skill.Metadata.PrimaryEnv);
        Assert.Equal("♊️", skill.Metadata.Emoji);
    }

    [Fact]
    public void ParseSkillContent_UserInvocableFalse_SetsProperly()
    {
        var content = """
            ---
            name: internal-skill
            description: Not user-invocable
            user-invocable: false
            ---
            Internal instructions.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/internal", SkillSource.Bundled);

        Assert.NotNull(skill);
        Assert.False(skill!.UserInvocable);
    }

    [Fact]
    public void ParseSkillContent_DisableModelInvocation_SetsProperly()
    {
        var content = """
            ---
            name: slash-only
            description: Slash command only
            disable-model-invocation: true
            ---
            Only via slash command.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/slash", SkillSource.Bundled);

        Assert.NotNull(skill);
        Assert.True(skill!.DisableModelInvocation);
    }

    [Fact]
    public void ParseSkillContent_CommandDispatch_SetsProperly()
    {
        var content = """
            ---
            name: summarize
            description: Summarize content
            command-dispatch: tool
            command-tool: summarize_tool
            command-arg-mode: raw
            ---
            Summarization instructions.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/summarize", SkillSource.Workspace);

        Assert.NotNull(skill);
        Assert.Equal("tool", skill!.CommandDispatch);
        Assert.Equal("summarize_tool", skill.CommandTool);
        Assert.Equal("raw", skill.CommandArgMode);
    }

    [Fact]
    public void ParseSkillContent_ReplacesBaseDir()
    {
        var content = """
            ---
            name: my-skill
            description: Uses baseDir
            ---
            Run the script at {baseDir}/run.sh
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/home/user/skills/my-skill", SkillSource.Workspace);

        Assert.NotNull(skill);
        Assert.Contains("/home/user/skills/my-skill/run.sh", skill!.Instructions);
        Assert.DoesNotContain("{baseDir}", skill.Instructions);
    }

    [Fact]
    public void ParseSkillContent_WithOsGate_ParsesOsList()
    {
        var content = """
            ---
            name: mac-only
            description: macOS only skill
            metadata: {"openclaw": {"os": ["darwin"]}}
            ---
            macOS instructions.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/mac", SkillSource.Bundled);

        Assert.NotNull(skill);
        Assert.Single(skill!.Metadata.Os);
        Assert.Equal("darwin", skill.Metadata.Os[0]);
    }

    [Fact]
    public void ParseSkillContent_AlwaysTrue_SetsFlag()
    {
        var content = """
            ---
            name: core-skill
            description: Always loaded
            metadata: {"openclaw": {"always": true}}
            ---
            Core instructions.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/core", SkillSource.Bundled);

        Assert.NotNull(skill);
        Assert.True(skill!.Metadata.Always);
    }

    [Fact]
    public void ParseMetadata_Null_ReturnsDefaults()
    {
        var meta = SkillLoader.ParseMetadata(null);
        Assert.False(meta.Always);
        Assert.Empty(meta.Os);
        Assert.Empty(meta.RequireBins);
        Assert.Empty(meta.RequireEnv);
    }

    [Fact]
    public void ParseMetadata_InvalidJson_ReturnsDefaults()
    {
        var meta = SkillLoader.ParseMetadata("not json at all");
        Assert.False(meta.Always);
    }

    [Fact]
    public void ParseMetadata_NoOpenclawKey_ReturnsDefaults()
    {
        var meta = SkillLoader.ParseMetadata("""{"other": true}""");
        Assert.False(meta.Always);
    }

    [Fact]
    public void LoadAll_Disabled_ReturnsEmpty()
    {
        var config = new SkillsConfig { Enabled = false };
        var logger = new TestLogger();

        var skills = SkillLoader.LoadAll(config, null, logger);

        Assert.Empty(skills);
    }

    [Fact]
    public void LoadAll_NoDirectories_ReturnsEmpty()
    {
        var config = new SkillsConfig
        {
            Enabled = true,
            Load = new SkillLoadConfig { IncludeBundled = false, IncludeManaged = false }
        };
        var logger = new TestLogger();

        var skills = SkillLoader.LoadAll(config, "/nonexistent/workspace", logger);

        Assert.Empty(skills);
    }

    [Fact]
    public void LoadAll_WithSkillFiles_LoadsAndFilters()
    {
        // Create temp skill structure: <workspace>/skills/<skill-name>/SKILL.md
        var tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-test-skills-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempDir, "skills", "test-skill");
        Directory.CreateDirectory(skillDir);

        try
        {
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
                ---
                name: test-skill
                description: A test skill
                ---
                Test instructions here.
                """);

            var config = new SkillsConfig
            {
                Enabled = true,
                Load = new SkillLoadConfig { IncludeBundled = false, IncludeManaged = false }
            };
            var logger = new TestLogger();

            // Use tempDir as workspace skills
            var skills = SkillLoader.LoadAll(config, tempDir, logger);

            Assert.Single(skills);
            Assert.Equal("test-skill", skills[0].Name);
            Assert.Equal(SkillSource.Workspace, skills[0].Source);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadAll_DisabledByEntry_Excluded()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-test-skills-{Guid.NewGuid():N}");
        var skillDir = Path.Combine(tempDir, "skills", "disabled-skill");
        Directory.CreateDirectory(skillDir);

        try
        {
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
                ---
                name: disabled-skill
                description: Should be filtered out
                ---
                Instructions.
                """);

            var config = new SkillsConfig
            {
                Enabled = true,
                Load = new SkillLoadConfig { IncludeBundled = false, IncludeManaged = false },
                Entries = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["disabled-skill"] = new SkillEntryConfig { Enabled = false }
                }
            };
            var logger = new TestLogger();

            var skills = SkillLoader.LoadAll(config, tempDir, logger);

            Assert.Empty(skills);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadAll_WorkspaceOverridesManaged_HigherPrecedenceWins()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-test-skills-{Guid.NewGuid():N}");
        var extraDir = Path.Combine(tempDir, "extra");
        var wsDir = Path.Combine(tempDir, "workspace");

        var extraSkillDir = Path.Combine(extraDir, "my-skill");
        var wsSkillDir = Path.Combine(wsDir, "skills", "my-skill");
        Directory.CreateDirectory(extraSkillDir);
        Directory.CreateDirectory(wsSkillDir);

        try
        {
            File.WriteAllText(Path.Combine(extraSkillDir, "SKILL.md"), """
                ---
                name: my-skill
                description: Extra version
                ---
                Extra instructions.
                """);

            File.WriteAllText(Path.Combine(wsSkillDir, "SKILL.md"), """
                ---
                name: my-skill
                description: Workspace version
                ---
                Workspace instructions.
                """);

            var config = new SkillsConfig
            {
                Enabled = true,
                Load = new SkillLoadConfig { ExtraDirs = [extraDir], IncludeBundled = false, IncludeManaged = false }
            };
            var logger = new TestLogger();

            var skills = SkillLoader.LoadAll(config, wsDir, logger);

            Assert.Single(skills);
            Assert.Equal("my-skill", skills[0].Name);
            Assert.Equal("Workspace version", skills[0].Description);
            Assert.Equal(SkillSource.Workspace, skills[0].Source);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadAll_ManagedSkill_IsDiscoveredFromDotOpenclaw()
    {
        var managedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".openclaw",
            "skills",
            $"managed-skill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(managedRoot);

        try
        {
            File.WriteAllText(Path.Combine(managedRoot, "SKILL.md"), """
                ---
                name: managed-skill
                description: Managed skill
                ---
                Managed instructions.
                """);

            var config = new SkillsConfig
            {
                Enabled = true,
                Load = new SkillLoadConfig { IncludeBundled = false, IncludeWorkspace = false }
            };
            var logger = new TestLogger();

            var skills = SkillLoader.LoadAll(config, null, logger);

            var skill = Assert.Single(skills, s => s.Name == "managed-skill");
            Assert.Equal(SkillSource.Managed, skill.Source);
        }
        finally
        {
            Directory.Delete(managedRoot, true);
        }
    }

    [Fact]
    public void LoadAll_ManagedRoot_TildePrefix_IsExpandedToUserHome()
    {
        var suffix = Path.Combine(".openclaw", "skills", $"managed-tilde-{Guid.NewGuid():N}");
        var managedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), suffix);
        Directory.CreateDirectory(managedRoot);

        try
        {
            File.WriteAllText(Path.Combine(managedRoot, "SKILL.md"), """
                ---
                name: managed-tilde-skill
                description: Managed tilde skill
                ---
                Managed tilde instructions.
                """);

            var config = new SkillsConfig
            {
                Enabled = true,
                Load = new SkillLoadConfig
                {
                    IncludeBundled = false,
                    IncludeWorkspace = false,
                    ManagedRoot = $"~/{suffix.Replace('\\', '/')}"
                }
            };
            var logger = new TestLogger();

            var skills = SkillLoader.LoadAll(config, null, logger);

            var skill = Assert.Single(skills, s => s.Name == "managed-tilde-skill");
            Assert.Equal(SkillSource.Managed, skill.Source);
        }
        finally
        {
            Directory.Delete(managedRoot, true);
        }
    }

    [Fact]
    public void LoadAll_ManagedRoot_RelativePath_IsResolvedFromCurrentDirectory()
    {
        var originalCwd = Directory.GetCurrentDirectory();
        var tempDir = Path.Combine(Path.GetTempPath(), $"skill-loader-relative-{Guid.NewGuid():N}");
        var relativeManagedRoot = "managed-relative";
        var absoluteManagedRoot = Path.Combine(tempDir, relativeManagedRoot);
        Directory.CreateDirectory(absoluteManagedRoot);

        try
        {
            File.WriteAllText(Path.Combine(absoluteManagedRoot, "SKILL.md"), """
                ---
                name: managed-relative-skill
                description: Managed relative skill
                ---
                Managed relative instructions.
                """);

            Directory.SetCurrentDirectory(tempDir);

            var config = new SkillsConfig
            {
                Enabled = true,
                Load = new SkillLoadConfig
                {
                    IncludeBundled = false,
                    IncludeWorkspace = false,
                    ManagedRoot = relativeManagedRoot
                }
            };
            var logger = new TestLogger();

            var skills = SkillLoader.LoadAll(config, null, logger);

            var skill = Assert.Single(skills, s => s.Name == "managed-relative-skill");
            Assert.Equal(SkillSource.Managed, skill.Source);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadAll_ManagedRoot_InvalidPath_DoesNotThrow()
    {
        var config = new SkillsConfig
        {
            Enabled = true,
            Load = new SkillLoadConfig
            {
                IncludeBundled = false,
                IncludeWorkspace = false,
                ManagedRoot = "invalid\0managed-root"
            }
        };
        var logger = new TestLogger();

        var skills = SkillLoader.LoadAll(config, null, logger);

        Assert.Empty(skills);
    }

    [Fact]
    public void ParseSkillContent_NoResourceDirs_ReturnsEmptyResources()
    {
        var content = """
            ---
            name: bare-skill
            description: No references or scripts
            ---
            Body.
            """;

        var skill = SkillLoader.ParseSkillContent(content, "/skills/bare-skill", SkillSource.Workspace);

        Assert.NotNull(skill);
        Assert.Empty(skill!.Resources);
    }

    [Fact]
    public void ParseSkillFile_WithReferencesAndScripts_PopulatesResources()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"openclaw-skill-resources-{Guid.NewGuid():N}");
        var referencesDir = Path.Combine(tempDir, "references");
        var scriptsDir = Path.Combine(tempDir, "scripts");
        var nestedDir = Path.Combine(referencesDir, "nested");
        Directory.CreateDirectory(referencesDir);
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(nestedDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "SKILL.md"), """
                ---
                name: rich-skill
                description: Has resources
                ---
                Body.
                """);
            File.WriteAllText(Path.Combine(referencesDir, "lookup.md"), "ref content");
            File.WriteAllText(Path.Combine(nestedDir, "deep.md"), "deep");
            File.WriteAllText(Path.Combine(scriptsDir, "run.sh"), "#!/bin/sh\n");

            var skill = SkillLoader.ParseSkillFile(
                Path.Combine(tempDir, "SKILL.md"),
                tempDir,
                SkillSource.Workspace);

            Assert.NotNull(skill);
            Assert.Equal(3, skill!.Resources.Count);

            var byPath = skill.Resources.ToDictionary(r => r.RelativePath, r => r);
            Assert.True(byPath.ContainsKey("references/lookup.md"));
            Assert.True(byPath.ContainsKey("references/nested/deep.md"));
            Assert.True(byPath.ContainsKey("scripts/run.sh"));

            Assert.Equal(SkillResourceKind.Reference, byPath["references/lookup.md"].Kind);
            Assert.Equal(SkillResourceKind.Reference, byPath["references/nested/deep.md"].Kind);
            Assert.Equal(SkillResourceKind.Script, byPath["scripts/run.sh"].Kind);

            Assert.Equal("lookup.md", byPath["references/lookup.md"].Name);
            Assert.True(File.Exists(byPath["references/lookup.md"].AbsolutePath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

public class SkillPromptBuilderTests
{
    [Fact]
    public void Build_NoSkills_ReturnsEmpty()
    {
        var result = SkillPromptBuilder.Build([]);
        Assert.Equal("", result);
    }

    [Fact]
    public void Build_WithSkills_GeneratesXml()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "web-search",
                Description = "Search the web",
                Instructions = "Use the web_search tool to find information.",
                Location = "/skills/web-search"
            }
        };

        var result = SkillPromptBuilder.Build(skills);

        Assert.Contains("<available-skills>", result);
        Assert.Contains("<name>web-search</name>", result);
        Assert.Contains("<description>Search the web</description>", result);
        Assert.Contains("<location>/skills/web-search</location>", result);
        Assert.Contains("</available-skills>", result);
        Assert.Contains("<skill-instructions>", result);
        Assert.Contains("## Skill: web-search", result);
        Assert.Contains("Use the web_search tool", result);
    }

    [Fact]
    public void Build_DisableModelInvocation_ExcludesSkill()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "visible",
                Description = "Visible skill",
                Instructions = "Visible instructions.",
                Location = "/skills/visible"
            },
            new()
            {
                Name = "hidden",
                Description = "Hidden skill",
                Instructions = "Hidden instructions.",
                Location = "/skills/hidden",
                DisableModelInvocation = true
            }
        };

        var result = SkillPromptBuilder.Build(skills);

        Assert.Contains("visible", result);
        Assert.DoesNotContain("<name>hidden</name>", result);
    }

    [Fact]
    public void Build_EscapesXmlChars()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "test & <demo>",
                Description = "A \"test\" skill",
                Instructions = "Instructions here.",
                Location = "/skills/test"
            }
        };

        var result = SkillPromptBuilder.Build(skills);

        Assert.Contains("test &amp; &lt;demo&gt;", result);
        Assert.Contains("A &quot;test&quot; skill", result);
    }

    [Fact]
    public void BuildSummary_NoSkills_ReturnsMessage()
    {
        var result = SkillPromptBuilder.BuildSummary([]);
        Assert.Equal("No skills loaded.", result);
    }

    [Fact]
    public void BuildSummary_WithSkills_ListsThem()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "search",
                Description = "Web search",
                Instructions = "...",
                Location = "/skills/search",
                Source = SkillSource.Workspace
            },
            new()
            {
                Name = "internal",
                Description = "Internal only",
                Instructions = "...",
                Location = "/skills/internal",
                Source = SkillSource.Bundled,
                DisableModelInvocation = true
            }
        };

        var result = SkillPromptBuilder.BuildSummary(skills);

        Assert.Contains("Loaded skills (2)", result);
        Assert.Contains("search: Web search", result);
        Assert.Contains("(Workspace)", result);
        Assert.Contains("internal: Internal only", result);
        Assert.Contains("[no-model]", result);
        Assert.Contains("(Bundled)", result);
    }

    [Fact]
    public void EstimateCharacterCost_NoSkills_ReturnsZero()
    {
        Assert.Equal(0, SkillPromptBuilder.EstimateCharacterCost([]));
    }

    [Fact]
    public void EstimateCharacterCost_WithSkills_ReturnsPositive()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "test",
                Description = "Test skill",
                Instructions = "Do the thing.",
                Location = "/skills/test"
            }
        };

        var cost = SkillPromptBuilder.EstimateCharacterCost(skills);
        Assert.True(cost > 195); // base + per-skill
    }

    [Fact]
    public void EstimateCharacterCost_ExcludesDisabledModelSkills()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "hidden",
                Description = "Hidden",
                Instructions = "...",
                Location = "/skills/hidden",
                DisableModelInvocation = true
            }
        };

        Assert.Equal(0, SkillPromptBuilder.EstimateCharacterCost(skills));
    }

    [Fact]
    public void BuildIndex_NoSkills_ReturnsEmpty()
    {
        Assert.Equal("", SkillPromptBuilder.BuildIndex([]));
    }

    [Fact]
    public void BuildIndex_OmitsInstructions_AndPointsAtLoadSkillTool()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "web-search",
                Description = "Search the web",
                Instructions = "Use the web_search tool to find information.",
                Location = "/skills/web-search"
            }
        };

        var index = SkillPromptBuilder.BuildIndex(skills);

        Assert.Contains("<available-skills>", index);
        Assert.Contains("<name>web-search</name>", index);
        Assert.Contains("<description>Search the web</description>", index);
        Assert.Contains("`load_skill`", index);
        // Critical: instructions body must NOT leak into the index.
        Assert.DoesNotContain("<skill-instructions>", index);
        Assert.DoesNotContain("Use the web_search tool", index);
    }

    [Fact]
    public void BuildIndex_IncludesResourceManifest()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "rich",
                Description = "Has resources",
                Instructions = "...",
                Location = "/skills/rich",
                Resources =
                [
                    new SkillResource
                    {
                        Name = "lookup.md",
                        RelativePath = "references/lookup.md",
                        AbsolutePath = "/skills/rich/references/lookup.md",
                        Kind = SkillResourceKind.Reference
                    },
                    new SkillResource
                    {
                        Name = "run.sh",
                        RelativePath = "scripts/run.sh",
                        AbsolutePath = "/skills/rich/scripts/run.sh",
                        Kind = SkillResourceKind.Script
                    }
                ]
            }
        };

        var index = SkillPromptBuilder.BuildIndex(skills);

        Assert.Contains("<resources>", index);
        Assert.Contains("kind=\"reference\"", index);
        Assert.Contains("path=\"references/lookup.md\"", index);
        Assert.Contains("kind=\"script\"", index);
        Assert.Contains("path=\"scripts/run.sh\"", index);
    }

    [Fact]
    public void BuildIndex_ExcludesDisableModelInvocation()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "hidden",
                Description = "Hidden",
                Instructions = "...",
                Location = "/skills/hidden",
                DisableModelInvocation = true
            }
        };

        Assert.Equal("", SkillPromptBuilder.BuildIndex(skills));
    }

    [Fact]
    public void BuildSkillBody_ReturnsInstructionsFragment()
    {
        var skill = new SkillDefinition
        {
            Name = "search",
            Description = "Search",
            Instructions = "Do the thing.",
            Location = "/skills/search"
        };

        var body = SkillPromptBuilder.BuildSkillBody(skill);

        Assert.Contains("<skill-instructions>", body);
        Assert.Contains("## Skill: search", body);
        Assert.Contains("Do the thing.", body);
        Assert.Contains("</skill-instructions>", body);
    }

    [Fact]
    public void BuildSkillBody_DisableModelInvocation_ReturnsEmpty()
    {
        var skill = new SkillDefinition
        {
            Name = "hidden",
            Description = "Hidden",
            Instructions = "Hidden body.",
            Location = "/skills/hidden",
            DisableModelInvocation = true
        };

        Assert.Equal("", SkillPromptBuilder.BuildSkillBody(skill));
    }

    [Fact]
    public void BuildIndex_CustomTemplate_ReplacesSkillsPlaceholder()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "alpha",
                Description = "Alpha skill",
                Instructions = "...",
                Location = "/skills/alpha"
            }
        };

        var template = "<skills-section>\n{skills}\n</skills-section>";

        var index = SkillPromptBuilder.BuildIndex(skills, template);

        Assert.Contains("<skills-section>", index);
        Assert.Contains("</skills-section>", index);
        Assert.Contains("<name>alpha</name>", index);
        // The default envelope must NOT leak in when a custom template is supplied.
        Assert.DoesNotContain("<available-skills>", index);
        Assert.DoesNotContain("Only load what is needed", index);
    }

    [Fact]
    public void BuildIndex_CustomTemplate_ReplacesLoadAndResourceInstructions()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "rich",
                Description = "Has resources",
                Instructions = "...",
                Location = "/skills/rich",
                Resources =
                [
                    new SkillResource
                    {
                        Name = "ref.md",
                        RelativePath = "references/ref.md",
                        AbsolutePath = "/skills/rich/references/ref.md",
                        Kind = SkillResourceKind.Reference
                    }
                ]
            }
        };

        const string template = "PRELUDE\n{load_instruction}{resource_instruction}---\n{skills}\nEND";

        var index = SkillPromptBuilder.BuildIndex(skills, template);

        Assert.Contains("PRELUDE", index);
        Assert.Contains("END", index);
        Assert.Contains("`load_skill`", index);
        Assert.Contains("`read_skill_resource`", index);
        Assert.Contains("<name>rich</name>", index);
    }

    [Fact]
    public void BuildIndex_CustomTemplate_OmitsResourceInstructionWhenNoResources()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "plain",
                Description = "No resources",
                Instructions = "...",
                Location = "/skills/plain"
            }
        };

        const string template = "{load_instruction}{resource_instruction}---{skills}";

        var index = SkillPromptBuilder.BuildIndex(skills, template);

        Assert.Contains("`load_skill`", index);
        Assert.DoesNotContain("`read_skill_resource`", index);
    }

    [Fact]
    public void BuildIndex_CustomTemplate_MissingSkillsPlaceholder_Throws()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "alpha",
                Description = "Alpha",
                Instructions = "...",
                Location = "/skills/alpha"
            }
        };

        var ex = Assert.Throws<ArgumentException>(
            () => SkillPromptBuilder.BuildIndex(skills, "no placeholder here"));

        Assert.Contains("{skills}", ex.Message);
    }

    [Fact]
    public void BuildIndex_NullOrWhitespaceTemplate_FallsBackToDefault()
    {
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "alpha",
                Description = "Alpha",
                Instructions = "...",
                Location = "/skills/alpha"
            }
        };

        var fromNull = SkillPromptBuilder.BuildIndex(skills, template: null);
        var fromBlank = SkillPromptBuilder.BuildIndex(skills, template: "   ");
        var defaultIndex = SkillPromptBuilder.BuildIndex(skills);

        Assert.Equal(defaultIndex, fromNull);
        Assert.Equal(defaultIndex, fromBlank);
        Assert.Contains("<available-skills>", defaultIndex);
    }

    [Fact]
    public void BuildIndex_NoEligibleSkills_IgnoresCustomTemplate()
    {
        // When all skills are excluded from the model, BuildIndex must return an empty
        // string regardless of any custom template supplied — including ones missing
        // the {skills} placeholder, which would otherwise throw.
        var skills = new List<SkillDefinition>
        {
            new()
            {
                Name = "hidden",
                Description = "Hidden",
                Instructions = "...",
                Location = "/skills/hidden",
                DisableModelInvocation = true
            }
        };

        Assert.Equal("", SkillPromptBuilder.BuildIndex(skills, "broken template"));
    }
}

public class LoadSkillToolTests
{
    private static SkillDefinition Skill(string name, string body = "Body.", bool disableModel = false,
        IReadOnlyList<SkillResource>? resources = null) =>
        new()
        {
            Name = name,
            Description = $"Description of {name}",
            Instructions = body,
            Location = $"/skills/{name}",
            DisableModelInvocation = disableModel,
            Resources = resources ?? []
        };

    [Fact]
    public async Task ExecuteAsync_ReturnsSkillBody_ForKnownSkill()
    {
        var tool = new LoadSkillTool([Skill("search", body: "Search instructions.")]);

        var result = await tool.ExecuteAsync("""{"skill":"search"}""", default);

        Assert.Contains("<skill-instructions>", result);
        Assert.Contains("## Skill: search", result);
        Assert.Contains("Search instructions.", result);
    }

    [Fact]
    public async Task ExecuteAsync_IsCaseInsensitive()
    {
        var tool = new LoadSkillTool([Skill("Search")]);

        var result = await tool.ExecuteAsync("""{"skill":"SEARCH"}""", default);

        Assert.Contains("## Skill: Search", result);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSkill_ReturnsErrorListingAvailable()
    {
        var tool = new LoadSkillTool([Skill("alpha"), Skill("beta")]);

        var result = await tool.ExecuteAsync("""{"skill":"gamma"}""", default);

        Assert.Contains("not found", result);
        Assert.Contains("alpha", result);
        Assert.Contains("beta", result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingArgument_ReturnsError()
    {
        var tool = new LoadSkillTool([Skill("alpha")]);

        var result = await tool.ExecuteAsync("{}", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("missing required argument", result);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var tool = new LoadSkillTool([Skill("alpha")]);

        var result = await tool.ExecuteAsync("not-json", default);

        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledForModel_RejectsLoad()
    {
        var tool = new LoadSkillTool([Skill("hidden", disableModel: true)]);

        var result = await tool.ExecuteAsync("""{"skill":"hidden"}""", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("not available for model invocation", result);
    }

    [Fact]
    public async Task ExecuteAsync_AppendsResourceManifest_WhenPresent()
    {
        var resources = new List<SkillResource>
        {
            new()
            {
                Name = "guide.md",
                RelativePath = "references/guide.md",
                AbsolutePath = "/skills/rich/references/guide.md",
                Kind = SkillResourceKind.Reference
            }
        };
        var tool = new LoadSkillTool([Skill("rich", resources: resources)]);

        var result = await tool.ExecuteAsync("""{"skill":"rich"}""", default);

        Assert.Contains("<skill-instructions>", result);
        Assert.Contains("<skill-resources>", result);
        Assert.Contains("path=\"references/guide.md\"", result);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsSkillNameAlias()
    {
        var tool = new LoadSkillTool([Skill("alpha")]);

        var result = await tool.ExecuteAsync("""{"skill_name":"alpha"}""", default);

        Assert.Contains("## Skill: alpha", result);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesViaSkillKeyAlias()
    {
        var skill = new SkillDefinition
        {
            Name = "real-name",
            Description = "d",
            Instructions = "Body.",
            Location = "/skills/real-name",
            Metadata = new SkillMetadata { SkillKey = "alias" }
        };
        var tool = new LoadSkillTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"alias"}""", default);

        Assert.Contains("## Skill: real-name", result);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderIsReevaluatedPerCall()
    {
        var skills = new List<SkillDefinition> { Skill("first") };
        var tool = new LoadSkillTool(() => skills);

        var first = await tool.ExecuteAsync("""{"skill":"first"}""", default);
        Assert.Contains("## Skill: first", first);

        // Hot reload simulation: swap the loaded set.
        skills.Clear();
        skills.Add(Skill("second"));

        var second = await tool.ExecuteAsync("""{"skill":"second"}""", default);
        Assert.Contains("## Skill: second", second);

        var missing = await tool.ExecuteAsync("""{"skill":"first"}""", default);
        Assert.Contains("not found", missing);
    }
}

public class ReadSkillResourceToolTests : IDisposable
{
    private readonly string _root;

    public ReadSkillResourceToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "openclaw-readskill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore cleanup races */ }
    }

    private SkillDefinition WriteSkillWithResource(string skillName, string relativePath, string content,
        SkillResourceKind kind = SkillResourceKind.Reference, bool disableModel = false)
    {
        var skillDir = Path.Combine(_root, skillName);
        var fullPath = Path.Combine(skillDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);

        return new SkillDefinition
        {
            Name = skillName,
            Description = $"Description of {skillName}",
            Instructions = "Body.",
            Location = skillDir,
            DisableModelInvocation = disableModel,
            Resources =
            [
                new SkillResource
                {
                    Name = Path.GetFileName(fullPath),
                    RelativePath = relativePath,
                    AbsolutePath = fullPath,
                    Kind = kind
                }
            ]
        };
    }

    [Fact]
    public async Task ExecuteAsync_ReadsResourceByRelativePath()
    {
        var skill = WriteSkillWithResource("alpha", "references/guide.md", "Hello, world.");
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"references/guide.md"}""", default);

        Assert.Equal("Hello, world.", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsResourceByBareFileName()
    {
        var skill = WriteSkillWithResource("alpha", "references/guide.md", "Hi.");
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"guide.md"}""", default);

        Assert.Equal("Hi.", result);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsBackslashSeparator()
    {
        var skill = WriteSkillWithResource("alpha", "references/guide.md", "Body.");
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"references\\guide.md"}""", default);

        Assert.Equal("Body.", result);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSkill_ReturnsError()
    {
        var skill = WriteSkillWithResource("alpha", "references/guide.md", "Body.");
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"missing","resource":"guide.md"}""", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("not found", result);
        Assert.Contains("alpha", result);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownResource_ListsAvailable()
    {
        var skill = WriteSkillWithResource("alpha", "references/guide.md", "Body.");
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"missing.md"}""", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("references/guide.md", result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingArguments_ReturnError()
    {
        var tool = new ReadSkillResourceTool([WriteSkillWithResource("alpha", "references/guide.md", "x")]);

        var missingResource = await tool.ExecuteAsync("""{"skill":"alpha"}""", default);
        Assert.Contains("'resource'", missingResource);

        var missingSkill = await tool.ExecuteAsync("""{"resource":"guide.md"}""", default);
        Assert.Contains("'skill'", missingSkill);

        var emptyArgs = await tool.ExecuteAsync("", default);
        Assert.StartsWith("Error:", emptyArgs);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledSkill_RejectsRead()
    {
        var skill = WriteSkillWithResource("hidden", "references/g.md", "x", disableModel: true);
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"hidden","resource":"g.md"}""", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("not available for model invocation", result);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsResourceOutsideSkillRoot()
    {
        // Build a skill whose Resources entry points OUTSIDE the skill's Location — simulates a
        // post-discovery symlink or hand-crafted SkillDefinition.
        var skillDir = Path.Combine(_root, "alpha");
        Directory.CreateDirectory(skillDir);
        var outsidePath = Path.Combine(_root, "evil.md");
        File.WriteAllText(outsidePath, "secret");

        var skill = new SkillDefinition
        {
            Name = "alpha",
            Description = "d",
            Instructions = "b",
            Location = skillDir,
            Resources =
            [
                new SkillResource
                {
                    Name = "evil.md",
                    RelativePath = "../evil.md",
                    AbsolutePath = outsidePath,
                    Kind = SkillResourceKind.Reference
                }
            ]
        };
        var tool = new ReadSkillResourceTool([skill]);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"../evil.md"}""", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("outside skill root", result);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderIsReevaluatedPerCall()
    {
        var skill = WriteSkillWithResource("alpha", "references/g.md", "first");
        var skills = new List<SkillDefinition> { skill };
        var tool = new ReadSkillResourceTool(() => skills);

        var first = await tool.ExecuteAsync("""{"skill":"alpha","resource":"g.md"}""", default);
        Assert.Equal("first", first);

        // Simulate hot reload pointing to a different file.
        var skill2 = WriteSkillWithResource("alpha", "references/g.md", "second");
        skills.Clear();
        skills.Add(skill2);

        var second = await tool.ExecuteAsync("""{"skill":"alpha","resource":"g.md"}""", default);
        Assert.Equal("second", second);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsResourceExceedingCustomByteLimit()
    {
        var skill = WriteSkillWithResource("alpha", "references/big.md", new string('x', 1024));
        // Cap below file size to force the size-limit branch.
        var tool = new ReadSkillResourceTool([skill], maxResourceBytes: 256);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"big.md"}""", default);

        Assert.StartsWith("Error:", result);
        Assert.Contains("256", result);
        Assert.Contains("workspace file tools", result);
    }

    [Fact]
    public async Task ExecuteAsync_NonPositiveByteLimit_FallsBackToDefault()
    {
        // 256 KB default cap allows a 1 KB body even when caller passes 0/-1.
        var skill = WriteSkillWithResource("alpha", "references/g.md", new string('y', 1024));
        var toolFromZero = new ReadSkillResourceTool([skill], maxResourceBytes: 0);
        var toolFromNegative = new ReadSkillResourceTool([skill], maxResourceBytes: -42);

        var fromZero = await toolFromZero.ExecuteAsync("""{"skill":"alpha","resource":"g.md"}""", default);
        var fromNegative = await toolFromNegative.ExecuteAsync("""{"skill":"alpha","resource":"g.md"}""", default);

        Assert.DoesNotContain("Error:", fromZero);
        Assert.Equal(1024, fromZero.Length);
        Assert.DoesNotContain("Error:", fromNegative);
        Assert.Equal(1024, fromNegative.Length);
    }

    [Fact]
    public async Task ExecuteAsync_CustomByteLimit_AcceptsResourceUnderCap()
    {
        var skill = WriteSkillWithResource("alpha", "references/small.md", "short");
        var tool = new ReadSkillResourceTool([skill], maxResourceBytes: 1024);

        var result = await tool.ExecuteAsync("""{"skill":"alpha","resource":"small.md"}""", default);

        Assert.Equal("short", result);
    }

    [Fact]
    public void DefaultMaxResourceBytes_MatchesPublicConstant()
    {
        // Lock the public default so SkillsConfig.MaxResourceReadBytes can rely on it.
        Assert.Equal(256 * 1024, ReadSkillResourceTool.DefaultMaxResourceBytes);
    }
}

/// <summary>Minimal ILogger for tests.</summary>
file sealed class TestLogger : Microsoft.Extensions.Logging.ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) { }
}
