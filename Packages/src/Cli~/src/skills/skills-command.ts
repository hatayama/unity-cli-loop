/**
 * CLI command definitions for skills management.
 */

// CLI commands output to console by design
/* eslint-disable no-console */

import { Command } from 'commander';
import {
  getAllSkillStatuses,
  installAllSkills,
  uninstallAllSkills,
  getInstallDir,
  getTotalSkillCount,
} from './skills-manager.js';
import { TargetConfig, ALL_TARGET_IDS, getTargetConfig } from './target-config.js';

interface SkillsOptions {
  global?: boolean;
  claude?: boolean;
  codex?: boolean;
  cursor?: boolean;
  gemini?: boolean;
  agents?: boolean;
  windsurf?: boolean;
  antigravity?: boolean;
}

export function registerSkillsCommand(program: Command): void {
  const skillsCmd = program
    .command('skills')
    .description('Manage uloop skills for AI coding tools');

  skillsCmd
    .command('list')
    .description('List all uloop skills and their installation status')
    .option('-g, --global', 'Check global installation')
    .option('--claude', 'Check Claude Code installation')
    .option('--codex', 'Check Codex CLI installation')
    .option('--cursor', 'Check Cursor installation')
    .option('--gemini', 'Check Gemini CLI installation')
    .option('--agents', 'Check generic .agents installation')
    .option('--windsurf', 'Check Windsurf installation')
    .option('--antigravity', 'Check Antigravity installation')
    .action((options: SkillsOptions) => {
      const targets = resolveTargets(options);
      const global = options.global ?? false;
      listSkills(targets, global);
    });

  skillsCmd
    .command('install')
    .description('Install all uloop skills')
    .option('-g, --global', 'Install to global location')
    .option('--claude', 'Install to Claude Code')
    .option('--codex', 'Install to Codex CLI')
    .option('--cursor', 'Install to Cursor')
    .option('--gemini', 'Install to Gemini CLI')
    .option('--agents', 'Install to generic .agents target')
    .option('--windsurf', 'Install to Windsurf')
    .option('--antigravity', 'Install to Antigravity')
    .action((options: SkillsOptions) => {
      const targets = resolveTargets(options);
      if (targets.length === 0) {
        showTargetGuidance('install');
        return;
      }
      installSkills(targets, options.global ?? false);
    });

  skillsCmd
    .command('uninstall')
    .description('Uninstall all uloop skills')
    .option('-g, --global', 'Uninstall from global location')
    .option('--claude', 'Uninstall from Claude Code')
    .option('--codex', 'Uninstall from Codex CLI')
    .option('--cursor', 'Uninstall from Cursor')
    .option('--gemini', 'Uninstall from Gemini CLI')
    .option('--agents', 'Uninstall from generic .agents target')
    .option('--windsurf', 'Uninstall from Windsurf')
    .option('--antigravity', 'Uninstall from Antigravity')
    .action((options: SkillsOptions) => {
      const targets = resolveTargets(options);
      if (targets.length === 0) {
        showTargetGuidance('uninstall');
        return;
      }
      uninstallSkills(targets, options.global ?? false);
    });
}

function resolveTargets(options: SkillsOptions): TargetConfig[] {
  const targets: TargetConfig[] = [];
  if (options.claude) {
    targets.push(getTargetConfig('claude'));
  }
  if (options.codex) {
    targets.push(getTargetConfig('codex'));
  }
  if (options.cursor) {
    targets.push(getTargetConfig('cursor'));
  }
  if (options.gemini) {
    targets.push(getTargetConfig('gemini'));
  }
  if (options.agents) {
    targets.push(getTargetConfig('agents'));
  }
  if (options.windsurf) {
    targets.push(getTargetConfig('windsurf'));
  }
  if (options.antigravity) {
    targets.push(getTargetConfig('antigravity'));
  }
  return targets;
}

function showTargetGuidance(command: string): void {
  console.log(`\nPlease specify at least one target for '${command}':`);
  console.log('');
  console.log('Available targets:');
  console.log('  --claude        Claude Code (.claude/skills/)');
  console.log('  --codex         Codex CLI (.codex/skills/)');
  console.log('  --cursor        Cursor (.cursor/skills/)');
  console.log('  --gemini        Gemini CLI (.gemini/skills/)');
  console.log('  --agents        Other (.agents) (.agents/skills/)');
  console.log('  --windsurf      Windsurf (.agents/skills/)');
  console.log('  --antigravity   Antigravity (.agent/skills/)');
  console.log('');
  console.log('Options:');
  console.log('  -g, --global   Use global location');
  console.log('');
  console.log('Examples:');
  console.log(`  uloop skills ${command} --claude`);
  console.log(`  uloop skills ${command} --cursor --global`);
  console.log(`  uloop skills ${command} --claude --codex --cursor --gemini --agents`);
}

function listSkills(targets: TargetConfig[], global: boolean): void {
  const location = global ? 'Global' : 'Project';
  const targetConfigs = targets.length > 0 ? targets : ALL_TARGET_IDS.map(getTargetConfig);

  console.log(`\nuloop Skills Status:`);
  console.log('');

  for (const target of targetConfigs) {
    const dir = getInstallDir(target, global);

    console.log(`${target.displayName} (${location}):`);
    console.log(`Location: ${dir}`);
    console.log('='.repeat(50));

    const statuses = getAllSkillStatuses(target, global);

    for (const skill of statuses) {
      const icon = getStatusIcon(skill.status);
      const statusText = getStatusText(skill.status);
      console.log(`  ${icon} ${skill.name} (${statusText})`);
    }

    console.log('');
  }

  console.log(`Total: ${getTotalSkillCount()} skills`);
}

function getStatusIcon(status: string): string {
  switch (status) {
    case 'installed':
      return '\x1b[32m✓\x1b[0m';
    case 'outdated':
      return '\x1b[33m↑\x1b[0m';
    case 'not_installed':
      return '\x1b[31m✗\x1b[0m';
    default:
      return '?';
  }
}

function getStatusText(status: string): string {
  switch (status) {
    case 'installed':
      return 'installed';
    case 'outdated':
      return 'outdated';
    case 'not_installed':
      return 'not installed';
    default:
      return 'unknown';
  }
}

function installSkills(targets: TargetConfig[], global: boolean): void {
  const location = global ? 'global' : 'project';

  console.log(`\nInstalling uloop skills (${location})...`);
  console.log('');

  for (const target of targets) {
    const dir = getInstallDir(target, global);
    const result = installAllSkills(target, global);

    console.log(`${target.displayName}:`);
    console.log(`  \x1b[32m✓\x1b[0m Installed: ${result.installed}`);
    console.log(`  \x1b[33m↑\x1b[0m Updated: ${result.updated}`);
    console.log(`  \x1b[90m-\x1b[0m Skipped (up-to-date): ${result.skipped}`);
    if (result.deprecatedRemoved > 0) {
      console.log(`  \x1b[31m✗\x1b[0m Deprecated removed: ${result.deprecatedRemoved}`);
    }
    console.log(`  Location: ${dir}`);
    console.log('');
  }
}

function uninstallSkills(targets: TargetConfig[], global: boolean): void {
  const location = global ? 'global' : 'project';

  console.log(`\nUninstalling uloop skills (${location})...`);
  console.log('');

  for (const target of targets) {
    const dir = getInstallDir(target, global);
    const result = uninstallAllSkills(target, global);

    console.log(`${target.displayName}:`);
    console.log(`  \x1b[31m✗\x1b[0m Removed: ${result.removed}`);
    console.log(`  \x1b[90m-\x1b[0m Not found: ${result.notFound}`);
    console.log(`  Location: ${dir}`);
    console.log('');
  }
}
