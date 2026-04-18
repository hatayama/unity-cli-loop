/**
 * Target configuration for multi-AI tool support.
 * Supports Claude Code and Codex CLI, with extensibility for future targets.
 */

export type TargetId =
  | 'claude'
  | 'codex'
  | 'cursor'
  | 'gemini'
  | 'agents'
  | 'windsurf'
  | 'antigravity';

export interface TargetConfig {
  id: TargetId;
  displayName: string;
  projectDir: string;
  skillFileName: string;
}

const TARGET_CONFIGS: Record<TargetId, TargetConfig> = {
  claude: {
    id: 'claude',
    displayName: 'Claude Code',
    projectDir: '.claude',
    skillFileName: 'SKILL.md',
  },
  codex: {
    id: 'codex',
    displayName: 'Codex CLI',
    projectDir: '.codex',
    skillFileName: 'SKILL.md',
  },
  cursor: {
    id: 'cursor',
    displayName: 'Cursor',
    projectDir: '.cursor',
    skillFileName: 'SKILL.md',
  },
  gemini: {
    id: 'gemini',
    displayName: 'Gemini CLI',
    projectDir: '.gemini',
    skillFileName: 'SKILL.md',
  },
  agents: {
    id: 'agents',
    displayName: 'Other (.agents)',
    projectDir: '.agents',
    skillFileName: 'SKILL.md',
  },
  windsurf: {
    id: 'windsurf',
    displayName: 'Windsurf',
    projectDir: '.agents',
    skillFileName: 'SKILL.md',
  },
  antigravity: {
    id: 'antigravity',
    displayName: 'Antigravity',
    projectDir: '.agent',
    skillFileName: 'SKILL.md',
  },
};

export const ALL_TARGET_IDS: TargetId[] = [
  'claude',
  'codex',
  'cursor',
  'gemini',
  'agents',
  'antigravity',
];

export function getTargetConfig(id: TargetId): TargetConfig {
  // eslint-disable-next-line security/detect-object-injection -- id is type-constrained to TargetId union type
  return TARGET_CONFIGS[id];
}
