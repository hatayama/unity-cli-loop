/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  moduleFileExtensions: ['ts', 'js', 'json', 'md'],
  testMatch: ['**/__tests__/**/*.test.ts'],
  transform: {
    '^.+\\.ts$': 'ts-jest',
    // commander v15 is ESM-only; transpile it to CJS so Jest's CJS runtime can load it
    '^.+\\.js$': ['ts-jest', { tsconfig: { allowJs: true } }],
    '^.+\\.md$': '<rootDir>/md-transformer.cjs',
  },
  transformIgnorePatterns: ['[\\\\/]node_modules[\\\\/](?!commander[\\\\/])'],
  moduleNameMapper: {
    '^(\\.{1,2}/.*)\\.js$': '$1',
  },
  collectCoverageFrom: [
    'src/**/*.ts',
    '!src/**/*.d.ts',
    '!src/**/__tests__/**',
  ],
  setupFilesAfterEnv: ['<rootDir>/src/__tests__/setup.ts'],
};
