import { getTargetConfig } from '../skills/target-config.js';

describe('target-config', () => {
  it('should store Codex skills under .codex', () => {
    const target = getTargetConfig('codex');

    expect(target.displayName).toBe('Codex CLI');
    expect(target.projectDir).toBe('.codex');
  });

  it('should store Gemini skills under .gemini', () => {
    const target = getTargetConfig('gemini');

    expect(target.displayName).toBe('Gemini CLI');
    expect(target.projectDir).toBe('.gemini');
  });

  it('should keep the generic agents target under .agents', () => {
    const target = getTargetConfig('agents');

    expect(target.displayName).toBe('Other (.agents)');
    expect(target.projectDir).toBe('.agents');
  });
});
