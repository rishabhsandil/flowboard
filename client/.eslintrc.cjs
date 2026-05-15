/* ESLint config for the FlowBoard React/TS client.
 * - Uses the legacy `.eslintrc` format because eslint 8 is the last version
 *   that plays nicely with our current React 18 + TS toolchain.
 * - `eslint-config-prettier` MUST stay last so it disables stylistic rules
 *   that would conflict with Prettier formatting.
 */
module.exports = {
  root: true,
  env: { browser: true, es2022: true, node: true },
  parser: '@typescript-eslint/parser',
  parserOptions: {
    ecmaVersion: 'latest',
    sourceType: 'module',
    ecmaFeatures: { jsx: true },
  },
  settings: { react: { version: '18.3' } },
  plugins: ['@typescript-eslint', 'react', 'react-hooks', 'react-refresh'],
  extends: [
    'eslint:recommended',
    'plugin:@typescript-eslint/recommended',
    'plugin:react/recommended',
    'plugin:react/jsx-runtime',
    'plugin:react-hooks/recommended',
    'prettier',
  ],
  ignorePatterns: ['dist', 'node_modules', '*.config.js', '*.config.cjs'],
  rules: {
    // Match coding-standards skill rule B1: no implicit any.
    '@typescript-eslint/no-explicit-any': 'warn',
    '@typescript-eslint/no-unused-vars': [
      'warn',
      { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
    ],
    'react/prop-types': 'off',
    // The UI uses "// label" as a stylistic mono-font prefix (not a real
    // comment). The rule has too many false positives for our design tokens.
    'react/jsx-no-comment-textnodes': 'off',
    // Apostrophes inside copy ("doesn't", "we're") are fine.
    'react/no-unescaped-entities': 'off',
    // Mirroring server data into local state via useEffect is a documented
    // pattern in this app (drag-and-drop optimistic updates).
    'react-hooks/set-state-in-effect': 'off',
    'react-refresh/only-export-components': [
      'warn',
      { allowConstantExport: true },
    ],
  },
};
