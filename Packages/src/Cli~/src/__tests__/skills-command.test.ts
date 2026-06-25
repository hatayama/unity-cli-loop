interface MockInstallResult {
  installed: number;
  updated: number;
  skipped: number;
  bundledCount: number;
  projectCount: number;
  deprecatedRemoved: number;
}

interface MockUninstallResult {
  removed: number;
  notFound: number;
}

const mockGetAllSkillStatuses = jest.fn<unknown[], [unknown, boolean, boolean]>();
const mockInstallAllSkills = jest.fn<MockInstallResult, [unknown, boolean, boolean, unknown?]>();
const mockUninstallAllSkills = jest.fn<MockUninstallResult, [unknown, boolean, boolean]>();
const mockGetInstallDir = jest.fn<string, [unknown, boolean, boolean]>();
const mockGetTotalSkillCount = jest.fn<number, []>();

jest.mock('../skills/skills-manager.js', () => ({
  DEFAULT_GROUP_MANAGED_SKILLS: false,
  getAllSkillStatuses: (target: unknown, global: boolean, groupManagedSkills: boolean): unknown[] =>
    mockGetAllSkillStatuses(target, global, groupManagedSkills),
  installAllSkills: (
    target: unknown,
    global: boolean,
    groupManagedSkills: boolean,
    cliInvocation?: unknown,
  ): MockInstallResult => mockInstallAllSkills(target, global, groupManagedSkills, cliInvocation),
  uninstallAllSkills: (
    target: unknown,
    global: boolean,
    groupManagedSkills: boolean,
  ): MockUninstallResult => mockUninstallAllSkills(target, global, groupManagedSkills),
  getInstallDir: (target: unknown, global: boolean, groupManagedSkills: boolean): string =>
    mockGetInstallDir(target, global, groupManagedSkills),
  getTotalSkillCount: (): number => mockGetTotalSkillCount(),
}));

import { Command } from 'commander';
import { registerSkillsCommand } from '../skills/skills-command.js';

describe('skills command', () => {
  let consoleLogSpy: jest.SpyInstance;

  beforeEach(() => {
    jest.clearAllMocks();
    consoleLogSpy = jest.spyOn(console, 'log').mockImplementation(() => undefined);
    mockInstallAllSkills.mockReturnValue({
      installed: 1,
      updated: 0,
      skipped: 0,
      bundledCount: 1,
      projectCount: 0,
      deprecatedRemoved: 0,
    });
    mockUninstallAllSkills.mockReturnValue({ removed: 1, notFound: 0 });
    mockGetInstallDir.mockReturnValue('/home/user/.claude/skills');
    mockGetTotalSkillCount.mockReturnValue(1);
  });

  afterEach(() => {
    consoleLogSpy.mockRestore();
  });

  it('installs Claude Code skills into the discoverable flat layout by default', async () => {
    // Verifies the default CLI path matches Claude Code skill discovery.
    const program = new Command();
    registerSkillsCommand(program);

    await program.parseAsync(['node', 'uloop', 'skills', 'install', '--claude', '--global']);

    expect(mockInstallAllSkills).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'claude' }),
      true,
      false,
      undefined,
    );
  });

  it('passes npx invocation to project installs', async () => {
    const program = new Command();
    registerSkillsCommand(program);

    await program.parseAsync([
      'node',
      'uloop',
      'skills',
      'install',
      '--claude',
      '--cli-invocation',
      'npx',
    ]);

    expect(mockInstallAllSkills).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'claude' }),
      false,
      false,
      'npx',
    );
  });

  it('rejects npx invocation for global installs', async () => {
    const program = new Command();
    registerSkillsCommand(program);

    await program.parseAsync([
      'node',
      'uloop',
      'skills',
      'install',
      '--claude',
      '--global',
      '--cli-invocation',
      'npx',
    ]);

    expect(mockInstallAllSkills).not.toHaveBeenCalled();
    expect(consoleLogSpy).toHaveBeenCalledWith(
      'The --cli-invocation npx option is only available for project installs.',
    );
  });
});
