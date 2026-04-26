/**
 * List of deprecated skill names that should be automatically removed during install.
 *
 * When a skill is renamed or removed, add the old name here to ensure
 * users don't end up with stale skill folders after updating.
 */
export const DEPRECATED_SKILLS: string[] = [
  'uloop-capture-window', // renamed to uloop-screenshot in v0.54.0
  'uloop-get-provider-details', // renamed to uloop-get-unity-search-providers
  'uloop-unity-search', // removed: replaceable by execute-dynamic-code
  'uloop-get-menu-items', // removed: replaceable by execute-dynamic-code
  'uloop-get-unity-search-providers', // removed: replaceable by execute-dynamic-code
  'uloop-execute-menu-item', // removed: replaceable by execute-dynamic-code
];
