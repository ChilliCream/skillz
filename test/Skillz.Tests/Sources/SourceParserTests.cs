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
        var result = SourceParser.ParseInternal("https://git.corp.com/group/subgroup/project/-/tree/main/src");
        Assert.Equal(
            new ParsedSource.GitLab("https://git.corp.com/group/subgroup/project.git", "main", "src"),
            result);
    }

    [Fact]
    public void GitLab_TreeWithBranchNoPath()
    {
        var result = SourceParser.ParseInternal("https://gitlab.example.com/org/repo/-/tree/v1.0");
        Assert.Equal(
            new ParsedSource.GitLab("https://gitlab.example.com/org/repo.git", "v1.0"),
            result);
    }

    [Fact]
    public void GitLab_CustomDomainWithPort()
    {
        var result = SourceParser.ParseInternal("https://git.corp.com:8443/group/repo/-/tree/main");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://git.corp.com:8443/group/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
    }

    [Fact]
    public void GitLab_HttpProtocolNonSsl()
    {
        var result = SourceParser.ParseInternal("http://git.local/group/repo/-/tree/dev");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("http://git.local/group/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLab_PersonalProjectPath()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/~user/project/-/tree/main");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/~user/project.git", gitlab.Url);
    }

    [Fact]
    public void SimplifiedGit_CustomDomainWithDotGit_IsGenericGit()
    {
        var result = SourceParser.ParseInternal("https://git.mycompany.com/my-group/my-repo.git");
        Assert.Equal(new ParsedSource.Git("https://git.mycompany.com/my-group/my-repo.git"), result);
    }

    [Fact]
    public void SimplifiedGit_GenericUrlsFallThroughToWellKnown()
    {
        var result = SourceParser.ParseInternal("https://google.com/search/result");
        var wellKnown = Assert.IsType<ParsedSource.WellKnown>(result);
        Assert.Equal("https://google.com/search/result", wellKnown.Url);
    }

    [Fact]
    public void SimplifiedGit_OfficialGitlabComStillParsed()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/owner/repo");
        Assert.Equal(new ParsedSource.GitLab("https://gitlab.com/owner/repo.git"), result);
    }

    [Fact]
    public void GitHub_Shorthand()
    {
        var result = SourceParser.ParseInternal("vercel-labs/agent-skills");
        Assert.Equal(new ParsedSource.GitHub("https://github.com/vercel-labs/agent-skills.git"), result);
    }

    [Fact]
    public void GitHub_FullUrlWithTreeAndPath()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo/tree/main/path");
        Assert.Equal(
            new ParsedSource.GitHub("https://github.com/owner/repo.git", "main", "path"),
            result);
    }

    [Fact]
    public void GitHub_BlobAnchorIsNotARef()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo/blob/main/README.md#L10");
        Assert.Equal(new ParsedSource.GitHub("https://github.com/owner/repo.git"), result);
    }

    [Fact]
    public void GitHub_ShorthandWithBranchFragment()
    {
        var result = SourceParser.ParseInternal("vercel-labs/agent-skills#feature/install");
        Assert.Equal(
            new ParsedSource.GitHub("https://github.com/vercel-labs/agent-skills.git", "feature/install"),
            result);
    }

    [Fact]
    public void GitHub_ShorthandTrailingSlash()
    {
        var result = SourceParser.ParseInternal("vercel-labs/agent-skills/");
        Assert.Equal(new ParsedSource.GitHub("https://github.com/vercel-labs/agent-skills.git"), result);
    }

    [Fact]
    public void Git_SshUrlWithBranchFragment()
    {
        var result = SourceParser.ParseInternal("git@github.com:owner/repo.git#feature/install");
        Assert.Equal(new ParsedSource.Git("git@github.com:owner/repo.git", "feature/install"), result);
    }

    [Fact]
    public void GitHub_BasicRepoUrl()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHub_RepoUrlWithDotGitSuffix()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo.git");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
    }

    [Fact]
    public void GitHub_RepoUrlWithDotGitAndBranchFragment()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo.git#feature/install");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature/install", github.Ref);
    }

    [Fact]
    public void GitHub_TreeWithBranchOnly()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo/tree/feature-branch");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature-branch", github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHub_TreeWithBranchAndPath()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo/tree/main/skills/my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("main", github.Ref);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHub_TreeWithSlashInPathAmbiguous()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo/tree/feature/my-feature");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature", github.Ref);
        Assert.Equal("my-feature", github.Subpath);
    }

    [Fact]
    public void GitLab_BasicRepoUrl()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/owner/repo");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
        Assert.Null(gitlab.Ref);
    }

    [Fact]
    public void GitLab_TreeWithBranchOnly()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/owner/repo/-/tree/develop");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
        Assert.Equal("develop", gitlab.Ref);
        Assert.Null(gitlab.Subpath);
    }

    [Fact]
    public void GitLab_TreeWithBranchAndPath()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/owner/repo/-/tree/main/src/skills");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
        Assert.Equal("src/skills", gitlab.Subpath);
    }

    [Fact]
    public void GitLab_RepoUrlWithDotGitSuffix()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/owner/repo.git");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLab_Subgroup2Levels()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/group/subgroup/repo");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
        Assert.Null(gitlab.Ref);
    }

    [Fact]
    public void GitLab_Subgroup3Levels()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/coresofthq/ai/agent-skills");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/coresofthq/ai/agent-skills.git", gitlab.Url);
        Assert.Null(gitlab.Ref);
    }

    [Fact]
    public void GitLab_DeepSubgroupWithDotGitSuffix()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/org/team/project/repo.git");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/org/team/project/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLab_SubgroupWithTreeBranch()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/group/subgroup/repo/-/tree/main");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
        Assert.Null(gitlab.Subpath);
    }

    [Fact]
    public void GitLab_SubgroupWithTreeBranchPath()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/group/subgroup/repo/-/tree/main/path/to/skill");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
        Assert.Equal("main", gitlab.Ref);
        Assert.Equal("path/to/skill", gitlab.Subpath);
    }

    [Fact]
    public void GitLab_TrailingSlash()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/group/subgroup/repo/");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepo()
    {
        var result = SourceParser.ParseInternal("owner/repo");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoPath()
    {
        var result = SourceParser.ParseInternal("owner/repo/skills/my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoTrailingSlash()
    {
        var result = SourceParser.ParseInternal("owner/repo/");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoAtSkill()
    {
        var result = SourceParser.ParseInternal("owner/repo@my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-skill", github.SkillFilter);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoAtHyphenatedSkill()
    {
        var result = SourceParser.ParseInternal("vercel-labs/agent-skills@find-skills");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/vercel-labs/agent-skills.git", github.Url);
        Assert.Equal("find-skills", github.SkillFilter);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoHashBranch()
    {
        var result = SourceParser.ParseInternal("owner/repo#my-branch");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-branch", github.Ref);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoPathHashBranch()
    {
        var result = SourceParser.ParseInternal("owner/repo/skills/my-skill#feature/skills");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature/skills", github.Ref);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHubShorthand_OwnerRepoHashBranchAtSkill()
    {
        var result = SourceParser.ParseInternal("owner/repo#my-branch@my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-branch", github.Ref);
        Assert.Equal("my-skill", github.SkillFilter);
    }

    [Fact]
    public void LocalPath_RelativeWithDotSlash()
    {
        var result = SourceParser.ParseInternal("./my-skills");
        var local = Assert.IsType<ParsedSource.Local>(result);
        Assert.Contains("my-skills", local.LocalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalPath_RelativeWithDotDotSlash()
    {
        var result = SourceParser.ParseInternal("../other-skills");
        var local = Assert.IsType<ParsedSource.Local>(result);
        Assert.Contains("other-skills", local.LocalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalPath_CurrentDirectory()
    {
        var result = SourceParser.ParseInternal(".");
        var local = Assert.IsType<ParsedSource.Local>(result);
        Assert.False(string.IsNullOrEmpty(local.LocalPath));
    }

    [Fact]
    public void LocalPath_Absolute()
    {
        var testPath = s_isWindows ? "C:\\Users\\test\\skills" : "/home/user/skills";
        var result = SourceParser.ParseInternal(testPath);
        var local = Assert.IsType<ParsedSource.Local>(result);
        Assert.Equal(testPath, local.LocalPath);
    }

    [Fact]
    public void Git_SshFormat()
    {
        var result = SourceParser.ParseInternal("git@github.com:owner/repo.git");
        Assert.Equal(new ParsedSource.Git("git@github.com:owner/repo.git"), result);
    }

    [Fact]
    public void Git_SshFormatWithBranch()
    {
        var result = SourceParser.ParseInternal("git@github.com:owner/repo.git#feature/install");
        Assert.Equal(
            new ParsedSource.Git("git@github.com:owner/repo.git", "feature/install"),
            result);
    }

    [Fact]
    public void Git_CustomHost()
    {
        var result = SourceParser.ParseInternal("https://git.example.com/owner/repo.git");
        Assert.Equal(new ParsedSource.Git("https://git.example.com/owner/repo.git"), result);
    }

    [Fact]
    public void Git_HttpsWithBranch()
    {
        var result = SourceParser.ParseInternal("https://git.example.com/owner/repo.git#release-2026");
        Assert.Equal(
            new ParsedSource.Git("https://git.example.com/owner/repo.git", "release-2026"),
            result);
    }

    [Fact]
    public void GetOwnerRepo_GitHubUrl()
    {
        var parsed = SourceParser.ParseInternal("https://github.com/owner/repo");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitHubUrlWithDotGit()
    {
        var parsed = SourceParser.ParseInternal("https://github.com/owner/repo.git");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitHubUrlWithTreeBranchPath()
    {
        var parsed = SourceParser.ParseInternal("https://github.com/owner/repo/tree/main/skills/my-skill");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitHubShorthand()
    {
        var parsed = SourceParser.ParseInternal("owner/repo");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitHubShorthandWithSubpath()
    {
        var parsed = SourceParser.ParseInternal("owner/repo/skills/my-skill");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitLabUrl()
    {
        var parsed = SourceParser.ParseInternal("https://gitlab.com/owner/repo");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitLabUrlWithTree()
    {
        var parsed = SourceParser.ParseInternal("https://gitlab.com/owner/repo/-/tree/main/skills");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitLabUrlWithSubgroup()
    {
        var parsed = SourceParser.ParseInternal("https://gitlab.com/coresofthq/ai/agent-skills");
        Assert.Equal("coresofthq/ai/agent-skills", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_LocalPathReturnsNull()
    {
        var parsed = SourceParser.ParseInternal("./my-skills");
        Assert.Null(OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_AbsoluteLocalPathReturnsNull()
    {
        var parsed = SourceParser.ParseInternal("/home/user/skills");
        Assert.Null(OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_CustomGitHost()
    {
        var parsed = SourceParser.ParseInternal("https://git.example.com/owner/repo.git");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshFormat()
    {
        var parsed = SourceParser.ParseInternal("git@github.com:owner/repo.git");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_PrivateGitLabInstance()
    {
        var parsed = SourceParser.ParseInternal("https://gitlab.company.com/team/repo");
        Assert.Equal("team/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SelfHostedGitWithDotGitSuffix()
    {
        var parsed = SourceParser.ParseInternal("https://git.internal.io/myteam/skills.git");
        Assert.Equal("myteam/skills", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitUrlWithQueryString()
    {
        var parsed = new ParsedSource.Git("https://git.example.com/owner/repo?ref=main");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitUrlWithFragment()
    {
        var parsed = new ParsedSource.Git("https://git.example.com/owner/repo#readme");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitUrlWithDotGitAndQueryString()
    {
        var parsed = new ParsedSource.Git("https://git.example.com/owner/repo.git?ref=main");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitLabSubgroup2Levels()
    {
        var parsed = new ParsedSource.Git("https://gitlab.com/group/subgroup/repo");
        Assert.Equal("group/subgroup/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitLabSubgroup3Levels()
    {
        var parsed = new ParsedSource.Git("https://gitlab.com/org/team/project/repo.git");
        Assert.Equal("org/team/project/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_GitLabSubgroupWithQueryString()
    {
        var parsed = new ParsedSource.Git("https://gitlab.com/group/subgroup/repo?ref=main");
        Assert.Equal("group/subgroup/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SelfHostedGitLabWithSubgroups()
    {
        var parsed = new ParsedSource.Git("https://gitlab.company.com/division/team/repo.git");
        Assert.Equal("division/team/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshUrlGitHub()
    {
        var parsed = new ParsedSource.Git("git@github.com:owner/repo.git");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshUrlGitLab()
    {
        var parsed = new ParsedSource.Git("git@gitlab.com:owner/repo.git");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshUrlWithSubgroupsGitLab()
    {
        var parsed = new ParsedSource.Git("git@gitlab.com:group/subgroup/project/repo.git");
        Assert.Equal("group/subgroup/project/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshUrlWithoutDotGitSuffix()
    {
        var parsed = new ParsedSource.Git("git@github.com:owner/repo");
        Assert.Equal("owner/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshUrlCustomHost()
    {
        var parsed = new ParsedSource.Git("git@git.company.com:org/team/repo.git");
        Assert.Equal("org/team/repo", OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GetOwnerRepo_SshUrlWithoutPathReturnsNull()
    {
        var parsed = new ParsedSource.Git("git@github.com:repo.git");
        Assert.Null(OwnerRepoParser.GetOwnerRepo(parsed));
    }

    [Fact]
    public void GitHubPrefix_Basic()
    {
        var result = SourceParser.ParseInternal("github:owner/repo");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Null(github.Subpath);
    }

    [Fact]
    public void GitHubPrefix_Subpath()
    {
        var result = SourceParser.ParseInternal("github:owner/repo/skills/my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void GitHubPrefix_AtSkillName()
    {
        var result = SourceParser.ParseInternal("github:owner/repo@my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("my-skill", github.SkillFilter);
    }

    [Fact]
    public void GitHubPrefix_GoogleWorkspaceCli()
    {
        var result = SourceParser.ParseInternal("github:googleworkspace/cli");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/googleworkspace/cli.git", github.Url);
    }

    [Fact]
    public void GitHubPrefix_HashBranch()
    {
        var result = SourceParser.ParseInternal("github:owner/repo#feature/install");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("https://github.com/owner/repo.git", github.Url);
        Assert.Equal("feature/install", github.Ref);
    }

    [Fact]
    public void GitLabPrefix_Basic()
    {
        var result = SourceParser.ParseInternal("gitlab:owner/repo");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/owner/repo.git", gitlab.Url);
    }

    [Fact]
    public void GitLabPrefix_GroupSubgroupRepo()
    {
        var result = SourceParser.ParseInternal("gitlab:group/subgroup/repo");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("https://gitlab.com/group/subgroup/repo.git", gitlab.Url);
    }

    [Fact]
    public void Subpath_GitHubTreeRejectsTraversal()
    {
        Assert.Throws<CliException>(
            () => SourceParser.ParseInternal("https://github.com/owner/repo/tree/main/../etc"));
    }

    [Fact]
    public void Subpath_GitHubTreeAllowsValid()
    {
        var result = SourceParser.ParseInternal("https://github.com/owner/repo/tree/main/skills/my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("skills/my-skill", github.Subpath);
    }

    [Fact]
    public void Subpath_GitLabTreeRejectsTraversal()
    {
        Assert.Throws<CliException>(
            () => SourceParser.ParseInternal("https://gitlab.com/owner/repo/-/tree/main/../etc"));
    }

    [Fact]
    public void Subpath_GitLabTreeAllowsValid()
    {
        var result = SourceParser.ParseInternal("https://gitlab.com/owner/repo/-/tree/main/src/skills");
        var gitlab = Assert.IsType<ParsedSource.GitLab>(result);
        Assert.Equal("src/skills", gitlab.Subpath);
    }

    [Fact]
    public void Subpath_GitHubShorthandRejectsTraversal()
    {
        Assert.Throws<CliException>(
            () => SourceParser.ParseInternal("owner/repo/../etc"));
    }

    [Fact]
    public void Subpath_GitHubShorthandAllowsValid()
    {
        var result = SourceParser.ParseInternal("owner/repo/skills/my-skill");
        var github = Assert.IsType<ParsedSource.GitHub>(result);
        Assert.Equal("skills/my-skill", github.Subpath);
    }
}
