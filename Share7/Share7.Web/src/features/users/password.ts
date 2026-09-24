// ===========================================================================
// Passwords for accounts an admin creates
//
// The server's rules are Identity's, configured in Share7.Infrastructure's
// AddInfrastructure: 8+ characters, with a lower-case letter, an upper-case
// letter and a digit. They are repeated here only to explain themselves while
// the admin types — the server still decides, and its refusal is shown as-is.
// ===========================================================================

/** What the password still lacks, as phrases that finish "Needs …". Empty when it will pass. */
export function passwordProblems(password: string): string[] {
  const problems: string[] = []
  if (password.length < 8) problems.push('at least 8 characters')
  if (!/[a-z]/.test(password)) problems.push('a lower-case letter')
  if (!/[A-Z]/.test(password)) problems.push('an upper-case letter')
  if (!/[0-9]/.test(password)) problems.push('a digit')
  return problems
}

// Characters that survive being read aloud or copied off a screen: no 0/O, 1/l/I.
const LOWER = 'abcdefghijkmnpqrstuvwxyz'
const UPPER = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
const DIGITS = '23456789'

/**
 * A random password that always meets the rules above.
 *
 * One character from each required class, the rest from all of them, then shuffled so the
 * required ones are not always in front. `crypto.getRandomValues` rather than `Math.random`: this
 * is a credential. The modulo bias on alphabets this small is negligible.
 */
export function generatePassword(length = 14): string {
  const pick = (set: string) => set[randomBelow(set.length)]

  const chars = [pick(LOWER), pick(UPPER), pick(DIGITS)]
  while (chars.length < length) chars.push(pick(LOWER + UPPER + DIGITS))

  for (let i = chars.length - 1; i > 0; i--) {
    const j = randomBelow(i + 1)
    ;[chars[i], chars[j]] = [chars[j], chars[i]]
  }

  return chars.join('')
}

function randomBelow(max: number): number {
  const buffer = new Uint32Array(1)
  crypto.getRandomValues(buffer)
  return buffer[0] % max
}
