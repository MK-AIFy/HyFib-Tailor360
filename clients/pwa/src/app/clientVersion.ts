/**
 * This build's version, as every request declares it in `X-Client-Version`.
 *
 * The server compares it with the minimum client it supports and answers `426 Upgrade Required` when
 * this build is too old (docs/architecture/conventions.md section 5.4). Declaring it on every request
 * is what turns "a field the server stopped reading" into one answer the client can act on.
 *
 * `__CLIENT_VERSION__` is replaced at build time from package.json. It is read through a guarded
 * `typeof` because a consumer that renders a component outside the Vite pipeline — a documentation
 * build, a snapshot tool — would otherwise throw a ReferenceError from a module every screen imports.
 * An unknown version is sent as nothing at all rather than as a guess: the server treats an absent
 * header as "not the progressive web application", which is the honest description of that caller.
 */
export const CLIENT_VERSION: string | undefined =
  typeof __CLIENT_VERSION__ === 'string' && __CLIENT_VERSION__.length > 0
    ? __CLIENT_VERSION__
    : undefined

/** The header the client declares its build in. */
export const CLIENT_VERSION_HEADER = 'X-Client-Version'
