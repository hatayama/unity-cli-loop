import { resolveCompileTargetProjectRoot } from '../../tools/compile-target-project-root.js';

describe('resolveCompileTargetProjectRoot', () => {
  it('prefers the final compile result ProjectRoot', () => {
    expect(
      resolveCompileTargetProjectRoot(
        {
          ProjectRoot: '/target-project',
        },
        '/immediate-project',
        '/logger-project',
      ),
    ).toBe('/target-project');
  });

  it('falls back to the immediate Unity ProjectRoot when the stored result omits it', () => {
    expect(resolveCompileTargetProjectRoot({}, '/immediate-project', '/logger-project')).toBe(
      '/immediate-project',
    );
  });

  it('falls back to the logger project root only when Unity never reports a target root', () => {
    expect(resolveCompileTargetProjectRoot({}, undefined, '/logger-project')).toBe(
      '/logger-project',
    );
  });

  it('ignores whitespace-only project roots before falling back', () => {
    expect(resolveCompileTargetProjectRoot({ ProjectRoot: '   ' }, '  ', '/logger-project')).toBe(
      '/logger-project',
    );
  });
});
