export function resolveCompileTargetProjectRoot(
  finalResult: unknown,
  projectRootFromUnity: string | undefined,
  fallbackProjectRoot: string,
): string {
  if (typeof finalResult === 'object' && finalResult !== null) {
    const resultRecord = finalResult as Record<string, unknown>;
    const projectRoot = resultRecord['ProjectRoot'];
    if (typeof projectRoot === 'string') {
      const normalizedProjectRoot = projectRoot.trim();
      if (normalizedProjectRoot.length > 0) {
        return normalizedProjectRoot;
      }
    }
  }

  if (typeof projectRootFromUnity === 'string') {
    const normalizedProjectRootFromUnity = projectRootFromUnity.trim();
    if (normalizedProjectRootFromUnity.length > 0) {
      return normalizedProjectRootFromUnity;
    }
  }

  return fallbackProjectRoot;
}
