using Skillz;
using Skillz.Sources;
using Xunit;

namespace Skillz.Tests.Sources;

public class SourceParserTests
{
    private static readonly bool s_isWindows = OperatingSystem.IsWindows();

    [Fact]
    public void GitLab_CustomDomain_DeepSubgroupPath()
    {
        // Act
        var result = new SourceParser().Parse("https://git.corp.com/group/subgroup/project/-/tree/main/src");

        // Assert
        Assert.Equal(new SkillSource.GitLab("https://git.corp.com/group/subgroup/project.git", "main", "src"), result);
    }

    [Fact]
    public void GitLab_TreeWithBranchNoPath()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.example.com/org/repo/-/tree/v1.0");

        // Assert
        Assert.Equal(new SkillSource.GitLab("https://gitlab.example.com/org/repo.git", "v1.0"), result);
    }

    [Fact]
    public void GitLab_CustomDomainWithPort()
    {
        // Act
        var result = new SourceParser().Parse("https://git.corp.com:8443/group/repo/-/tree/main");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://git.corp.com:8443/group/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
    }

    [Fact]
    public void GitLab_HttpProtocolNonSsl()
    {
        // Act
        var result = new SourceParser().Parse("http://git.local/group/repo/-/tree/dev");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("http://git.local/group/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLab_PersonalProjectPath()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/~user/project/-/tree/main");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/~user/project.git", gitlab.Url);
    }

    [Fact]
    public void SimplifiedGit_CustomDomainWithDotGit_IsGenericGit()
    {
        // Act
        var result = new SourceParser().Parse("https://git.mycompany.com/my-group/my-repo.git");

        // Assert
        Assert.Equal(new SkillSource.Git("https://git.mycompany.com/my-group/my-repo.git"), result);
    }

    [Fact]
    public void SimplifiedGit_GenericUrlsFallThroughToWellKnown()
    {
        // Act
        var result = new SourceParser().Parse("https://google.com/search/result");

        // Assert
        var wellKnown = Assert.IsType<SkillSource.WellKnown>(result);
        Assert.Equal("https://google.com/search/result", wellKnown.Url);
    }

    [Fact]
    public void SimplifiedGit_OfficialGitlabComStillParsed()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/owner/repo");

        // Assert
        Assert.Equal(new SkillSource.GitLab("https://gitlab.com/owner/repo.git"), result);
    }

    [Fact]
    public void GitHub_Shorthand()
    {
        // Act
        var result = new SourceParser().Parse("vercel-labs/agent-skills");

        // Assert
        Assert.Equal(new SkillSource.GitHub("https://github.com/vercel-labs/agent-skills.git"), result);
    }

    [Fact]
    public void GitHub_FullUrlWithTreeAndPath()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo/tree/main/path");

        // Assert
        Assert.Equal(new SkillSource.GitHub("https://github.com/owner/repo.git", "main", "path"), result);
    }

    [Fact]
    public void GitHub_BlobAnchorIsNotARef()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo/blob/main/README.md#L10");

        // Assert
        Assert.Equal(new SkillSource.GitHub("https://github.com/owner/repo.git"), result);
    }

    [Fact]
    public void GitHub_ShorthandWithBranchFragment()
    {
        // Act
        var result = new SourceParser().Parse("vercel-labs/agent-skills#feature/install");

        // Assert
        Assert.Equal(
            new SkillSource.GitHub("https://github.com/vercel-labs/agent-skills.git", "feature/install"),
            result);
    }

    [Fact]
    public void GitHub_ShorthandTrailingSlash()
    {
        // Act
        var result = new SourceParser().Parse("vercel-labs/agent-skills/");

        // Assert
        Assert.Equal(new SkillSource.GitHub("https://github.com/vercel-labs/agent-skills.git"), result);
    }

    [Fact]
    public void Git_SshUrlWithBranchFragment()
    {
        // Act
        var result = new SourceParser().Parse("git@github.com:owner/repo.git#feature/install");

        // Assert
        Assert.Equal(new SkillSource.Git("git@github.com:owner/repo.git", "feature/install"), result);
    }

    [Fact]
    public void GitHub_BasicRepoUrl()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHub_RepoUrlWithDotGitSuffix()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo.git");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
    }

    [Fact]
    public void GitHub_RepoUrlWithDotGitAndBranchFragment()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo.git#feature/install");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature/install", github.Ref);
    }

    [Fact]
    public void GitHub_TreeWithBranchOnly()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo/tree/feature-branch");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature-branch", github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHub_TreeWithBranchAndPath()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo/tree/main/skills/my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("main", github.Ref);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHub_TreeWithSlashInPathAmbiguous()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo/tree/feature/my-feature");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature", github.Ref);
        Assert.Equal("my-feature", github.Subpath);
    }

    [Fact]
    public void GitLab_BasicRepoUrl()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/owner/repo");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
        Assert.Null(gitlab.Ref);
    }

    [Fact]
    public void GitLab_TreeWithBranchOnly()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/owner/repo/-/tree/develop");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
        Assert.Equal("develop", gitlab.Ref);
        Assert.Null(gitlab.Subpath);
    }

    [Fact]
    public void GitLab_TreeWithBranchAndPath()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/owner/repo/-/tree/main/src/skills");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
        Assert.Equal("src/skills", gitlab.Subpath);
    }

    [Fact]
    public void GitLab_RepoUrlWithDotGitSuffix()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/owner/repo.git");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLab_Subgroup2Levels()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/group/subgroup/repo");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
        Assert.Null(gitlab.Ref);
    }

    [Fact]
    public void GitLab_Subgroup3Levels()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/coresofthq/ai/agent-skills");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/coresofthq/ai/agent-skills.git", gitlab.Url);
        Assert.Null(gitlab.Ref);
    }

    [Fact]
    public void GitLab_DeepSubgroupWithDotGitSuffix()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/org/team/project/repo.git");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/org/team/project/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLab_SubgroupWithTreeBranch()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/group/subgroup/repo/-/tree/main");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
        Assert.Null(gitlab.Subpath);
    }

    [Fact]
    public void GitLab_SubgroupWithTreeBranchPath()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/group/subgroup/repo/-/tree/main/path/to/skill");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
        Assert.Equal("path/to/skill", gitlab.Subpath);
    }

    [Fact]
    public void GitLab_TrailingSlash()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/group/subgroup/repo/");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepo()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoPath()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo/skills/my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoTrailingSlash()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo/");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoAtSkill()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo@my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-skill", github.SkillFilter);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoAtHyphenatedSkill()
    {
        // Act
        var result = new SourceParser().Parse("vercel-labs/agent-skills@find-skills");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/vercel-labs/agent-skills.git", github.Url);
        Assert.Equal("find-skills", github.SkillFilter);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoHashBranch()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo#my-branch");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-branch", github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoPathHashBranch()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo/skills/my-skill#feature/skills");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature/skills", github.Ref);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoHashBranchAtSkill()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo#my-branch@my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-branch", github.Ref);
        Assert.Equal("my-skill", github.SkillFilter);
    }

    [Fact]
    public void LocalPath_RelativeWithDotSlash()
    {
        // Act
        var result = new SourceParser().Parse("./my-skills");

        // Assert
        var local = Assert.IsType<SkillSource.Local>(result);
        Assert.Contains("my-skills", local.LocalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalPath_RelativeWithDotDotSlash()
    {
        // Act
        var result = new SourceParser().Parse("../other-skills");

        // Assert
        var local = Assert.IsType<SkillSource.Local>(result);
        Assert.Contains("other-skills", local.LocalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalPath_CurrentDirectory()
    {
        // Act
        var result = new SourceParser().Parse(".");

        // Assert
        var local = Assert.IsType<SkillSource.Local>(result);
        Assert.False(string.IsNullOrEmpty(local.LocalPath));
    }

    [Fact]
    public void LocalPath_Absolute()
    {
        // Arrange
        var testPath = s_isWindows ? "C:\\Users\\test\\skills" : "/home/user/skills";

        // Act
        var result = new SourceParser().Parse(testPath);

        // Assert
        var local = Assert.IsType<SkillSource.Local>(result);
        Assert.Equal(testPath, local.LocalPath);
    }

    [Fact]
    public void Git_SshFormat()
    {
        // Act
        var result = new SourceParser().Parse("git@github.com:owner/repo.git");

        // Assert
        Assert.Equal(new SkillSource.Git("git@github.com:owner/repo.git"), result);
    }

    [Fact]
    public void Git_SshFormatWithBranch()
    {
        // Act
        var result = new SourceParser().Parse("git@github.com:owner/repo.git#feature/install");

        // Assert
        Assert.Equal(new SkillSource.Git("git@github.com:owner/repo.git", "feature/install"), result);
    }

    [Fact]
    public void Git_CustomHost()
    {
        // Act
        var result = new SourceParser().Parse("https://git.example.com/owner/repo.git");

        // Assert
        Assert.Equal(new SkillSource.Git("https://git.example.com/owner/repo.git"), result);
    }

    [Fact]
    public void Git_HttpsWithBranch()
    {
        // Act
        var result = new SourceParser().Parse("https://git.example.com/owner/repo.git#release-2026");

        // Assert
        Assert.Equal(new SkillSource.Git("https://git.example.com/owner/repo.git", "release-2026"), result);
    }

    [Fact]
    public void FindOwnerRepo_GitHubUrl()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://github.com/owner/repo");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitHubUrlWithDotGit()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://github.com/owner/repo.git");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitHubUrlWithTreeBranchPath()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://github.com/owner/repo/tree/main/skills/my-skill");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitHubShorthand()
    {
        // Arrange
        var parsed = new SourceParser().Parse("owner/repo");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitHubShorthandWithSubpath()
    {
        // Arrange
        var parsed = new SourceParser().Parse("owner/repo/skills/my-skill");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitLabUrl()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://gitlab.com/owner/repo");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitLabUrlWithTree()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://gitlab.com/owner/repo/-/tree/main/skills");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitLabUrlWithSubgroup()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://gitlab.com/coresofthq/ai/agent-skills");

        // Act & Assert
        Assert.Equal("coresofthq/ai/agent-skills", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_LocalPathReturnsNull()
    {
        // Arrange
        var parsed = new SourceParser().Parse("./my-skills");

        // Act & Assert
        Assert.Null(OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_AbsoluteLocalPathReturnsNull()
    {
        // Arrange
        var parsed = new SourceParser().Parse("/home/user/skills");

        // Act & Assert
        Assert.Null(OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_CustomGitHost()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://git.example.com/owner/repo.git");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshFormat()
    {
        // Arrange
        var parsed = new SourceParser().Parse("git@github.com:owner/repo.git");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_PrivateGitLabInstance()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://gitlab.company.com/team/repo");

        // Act & Assert
        Assert.Equal("team/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SelfHostedGitWithDotGitSuffix()
    {
        // Arrange
        var parsed = new SourceParser().Parse("https://git.internal.io/myteam/skills.git");

        // Act & Assert
        Assert.Equal("myteam/skills", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitUrlWithQueryString()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://git.example.com/owner/repo?ref=main");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitUrlWithFragment()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://git.example.com/owner/repo#readme");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitUrlWithDotGitAndQueryString()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://git.example.com/owner/repo.git?ref=main");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitLabSubgroup2Levels()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://gitlab.com/group/subgroup/repo");

        // Act & Assert
        Assert.Equal("group/subgroup/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitLabSubgroup3Levels()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://gitlab.com/org/team/project/repo.git");

        // Act & Assert
        Assert.Equal("org/team/project/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_GitLabSubgroupWithQueryString()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://gitlab.com/group/subgroup/repo?ref=main");

        // Act & Assert
        Assert.Equal("group/subgroup/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SelfHostedGitLabWithSubgroups()
    {
        // Arrange
        var parsed = new SkillSource.Git("https://gitlab.company.com/division/team/repo.git");

        // Act & Assert
        Assert.Equal("division/team/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshUrlGitHub()
    {
        // Arrange
        var parsed = new SkillSource.Git("git@github.com:owner/repo.git");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshUrlGitLab()
    {
        // Arrange
        var parsed = new SkillSource.Git("git@gitlab.com:owner/repo.git");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshUrlWithSubgroupsGitLab()
    {
        // Arrange
        var parsed = new SkillSource.Git("git@gitlab.com:group/subgroup/project/repo.git");

        // Act & Assert
        Assert.Equal("group/subgroup/project/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshUrlWithoutDotGitSuffix()
    {
        // Arrange
        var parsed = new SkillSource.Git("git@github.com:owner/repo");

        // Act & Assert
        Assert.Equal("owner/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshUrlCustomHost()
    {
        // Arrange
        var parsed = new SkillSource.Git("git@git.company.com:org/team/repo.git");

        // Act & Assert
        Assert.Equal("org/team/repo", OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void FindOwnerRepo_SshUrlWithoutPathReturnsNull()
    {
        // Arrange
        var parsed = new SkillSource.Git("git@github.com:repo.git");

        // Act & Assert
        Assert.Null(OwnerRepoParser.FindOwnerRepo(parsed));
    }

    [Fact]
    public void GitHubPrefix_Basic()
    {
        // Act
        var result = new SourceParser().Parse("github:owner/repo");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubPrefix_Subpath()
    {
        // Act
        var result = new SourceParser().Parse("github:owner/repo/skills/my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHubPrefix_AtSkillName()
    {
        // Act
        var result = new SourceParser().Parse("github:owner/repo@my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-skill", github.SkillFilter);
    }

    [Fact]
    public void GitHubPrefix_GoogleWorkspaceCli()
    {
        // Act
        var result = new SourceParser().Parse("github:googleworkspace/cli");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/googleworkspace/cli.git", github.Url);
    }

    [Fact]
    public void GitHubPrefix_HashBranch()
    {
        // Act
        var result = new SourceParser().Parse("github:owner/repo#feature/install");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature/install", github.Ref);
    }

    [Fact]
    public void GitLabPrefix_Basic()
    {
        // Act
        var result = new SourceParser().Parse("gitlab:owner/repo");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLabPrefix_GroupSubgroupRepo()
    {
        // Act
        var result = new SourceParser().Parse("gitlab:group/subgroup/repo");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
    }

    [Fact]
    public void Subpath_GitHubTreeRejectsTraversal()
    {
        // Act & Assert
        Assert.Throws<CliException>(() => new SourceParser().Parse("https://github.com/owner/repo/tree/main/../etc"));
    }

    [Fact]
    public void Subpath_GitHubTreeAllowsValid()
    {
        // Act
        var result = new SourceParser().Parse("https://github.com/owner/repo/tree/main/skills/my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void Subpath_GitLabTreeRejectsTraversal()
    {
        // Act & Assert
        Assert.Throws<CliException>(() => new SourceParser().Parse("https://gitlab.com/owner/repo/-/tree/main/../etc"));
    }

    [Fact]
    public void Subpath_GitLabTreeAllowsValid()
    {
        // Act
        var result = new SourceParser().Parse("https://gitlab.com/owner/repo/-/tree/main/src/skills");

        // Assert
        var gitlab = Assert.IsType<SkillSource.GitLab>(result);
        Assert.Equal("src/skills", gitlab.Subpath);
    }

    [Fact]
    public void Subpath_GitHubShorthandRejectsTraversal()
    {
        // Act & Assert
        Assert.Throws<CliException>(() => new SourceParser().Parse("owner/repo/../etc"));
    }

    [Fact]
    public void Subpath_GitHubShorthandAllowsValid()
    {
        // Act
        var result = new SourceParser().Parse("owner/repo/skills/my-skill");

        // Assert
        var github = Assert.IsType<SkillSource.GitHub>(result);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Theory]
    [InlineData("owner/\u001brepo")]
    [InlineData("owner/repo\n")]
    [InlineData("owner/repo\trepo")]
    public void Parse_Should_Throw_When_SourceContainsControlCharacter(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => new SourceParser().Parse(input));

        // Assert
        Assert.Contains("control character", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#main")]
    [InlineData("#main@skill")]
    public void Parse_Should_Throw_When_SourceIsOnlyAFragment(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => new SourceParser().Parse(input));

        // Assert
        Assert.Contains("Missing required argument: source", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://host/repo.git")]
    [InlineData("ssh://git@host/repo")]
    [InlineData("git://host/repo")]
    public void Parse_Should_RouteToGit_When_TransportSchemeIsAllowed(string input)
    {
        // Act
        var result = new SourceParser().Parse(input);

        // Assert
        var git = Assert.IsType<SkillSource.Git>(result);
        Assert.Equal(input, git.Url);
    }

    [Fact]
    public void Parse_Should_RouteToGit_When_ScpStyleHostIsValid()
    {
        // Act
        var result = new SourceParser().Parse("git@github.com:owner/repo.git");

        // Assert
        Assert.Equal(new SkillSource.Git("git@github.com:owner/repo.git"), result);
    }

    [Theory]
    [InlineData("ext::sh -c 'id'")]
    [InlineData("fd::17/repo")]
    [InlineData("file:///etc/passwd")]
    [InlineData("git@-oProxyCommand=x:repo")]
    public void Parse_Should_Throw_When_TransportIsDisallowed(string input)
    {
        // Act
        var ex = Assert.Throws<CliException>(() => new SourceParser().Parse(input));

        // Assert
        Assert.Contains("Unsupported git transport", ex.Message, StringComparison.Ordinal);
    }
}
