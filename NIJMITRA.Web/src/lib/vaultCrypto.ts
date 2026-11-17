/**
 * Zero-knowledge vault cryptography (runs only in the browser, WebCrypto):
 *  - KEK = PBKDF2-SHA256(master password, 16-byte random salt, 600 000 iterations) → AES-256-GCM key
 *  - Vault key = random AES-256-GCM key, stored on the server only "wrapped" (encrypted) by the KEK
 *  - A one-time recovery key wraps the same vault key a second time
 *  - Every item: AES-256-GCM with a fresh random 96-bit IV, serialised as "v1.<iv>.<ciphertext>" (base64)
 * The master password, recovery key and vault key never leave the device.
 */
export const KDF_ITERATIONS = 600_000
const enc = new TextEncoder()
const dec = new TextDecoder()

export const b64 = (buf: ArrayBuffer | Uint8Array) => {
  const bytes = buf instanceof Uint8Array ? buf : new Uint8Array(buf)
  let s = ''
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]!)
  return btoa(s)
}
export const unb64 = (s: string) => Uint8Array.from(atob(s), (c) => c.charCodeAt(0))

async function deriveKek(secret: string, salt: Uint8Array, iterations: number) {
  const base = await crypto.subtle.importKey('raw', enc.encode(secret.normalize('NFKC')), 'PBKDF2', false, ['deriveKey'])
  return crypto.subtle.deriveKey({ name: 'PBKDF2', hash: 'SHA-256', salt: salt as BufferSource, iterations }, base, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
}

async function seal(key: CryptoKey, data: Uint8Array) {
  const iv = crypto.getRandomValues(new Uint8Array(12))
  const ct = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv as BufferSource }, key, data as BufferSource)
  return `v1.${b64(iv)}.${b64(ct)}`
}

async function open(key: CryptoKey, payload: string) {
  const [v, iv, ct] = payload.split('.')
  if (v !== 'v1' || !iv || !ct) throw new Error('Unsupported ciphertext')
  return new Uint8Array(await crypto.subtle.decrypt({ name: 'AES-GCM', iv: unb64(iv) as BufferSource }, key, unb64(ct) as BufferSource))
}

const importVaultKey = (raw: Uint8Array) => crypto.subtle.importKey('raw', raw as BufferSource, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt'])

export interface VaultHeader { kdfAlgorithm: string; kdfIterations: number; kdfSalt: string; wrappedKey: string; recoverySalt: string; recoveryWrappedKey: string }

export function generateRecoveryKey() {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  const bytes = crypto.getRandomValues(new Uint8Array(24))
  const chars = Array.from(bytes, (b) => alphabet[b % alphabet.length]).join('')
  return chars.match(/.{4}/g)!.join('-')
}

export async function createVault(master: string) {
  const raw = crypto.getRandomValues(new Uint8Array(32))
  const salt = crypto.getRandomValues(new Uint8Array(16))
  const rSalt = crypto.getRandomValues(new Uint8Array(16))
  const recoveryKey = generateRecoveryKey()
  const wrappedKey = await seal(await deriveKek(master, salt, KDF_ITERATIONS), raw)
  const recoveryWrappedKey = await seal(await deriveKek(recoveryKey.replace(/-/g, ''), rSalt, 200_000), raw)
  const key = await importVaultKey(raw)
  raw.fill(0)
  return { request: { kdfIterations: KDF_ITERATIONS, kdfSalt: b64(salt), wrappedKey, recoverySalt: b64(rSalt), recoveryWrappedKey }, recoveryKey, key }
}

/** Unwraps the vault key; returns null when the master password is wrong (GCM authentication fails). */
export async function unlockVault(master: string, h: VaultHeader): Promise<CryptoKey | null> {
  try {
    const raw = await open(await deriveKek(master, unb64(h.kdfSalt), h.kdfIterations), h.wrappedKey)
    const key = await importVaultKey(raw)
    raw.fill(0)
    return key
  } catch { return null }
}

async function unwrapRaw(secret: string, salt: string, iterations: number, wrapped: string) {
  return open(await deriveKek(secret, unb64(salt), iterations), wrapped)
}

/** Re-wraps the vault key under a new master password (requires current master or recovery key). */
export async function rekeyVault(h: VaultHeader, current: { master?: string; recoveryKey?: string }, newMaster: string) {
  let raw: Uint8Array
  try {
    raw = current.master
      ? await unwrapRaw(current.master, h.kdfSalt, h.kdfIterations, h.wrappedKey)
      : await unwrapRaw((current.recoveryKey ?? '').replace(/[-\s]/g, '').toUpperCase(), h.recoverySalt, 200_000, h.recoveryWrappedKey)
  } catch { return null }
  const salt = crypto.getRandomValues(new Uint8Array(16))
  const rSalt = crypto.getRandomValues(new Uint8Array(16))
  const recoveryKey = generateRecoveryKey()
  const wrappedKey = await seal(await deriveKek(newMaster, salt, KDF_ITERATIONS), raw)
  const recoveryWrappedKey = await seal(await deriveKek(recoveryKey.replace(/-/g, ''), rSalt, 200_000), raw)
  const key = await importVaultKey(raw)
  raw.fill(0)
  return { request: { kdfIterations: KDF_ITERATIONS, kdfSalt: b64(salt), wrappedKey, recoverySalt: b64(rSalt), recoveryWrappedKey }, recoveryKey, key }
}

export const encryptJson = async (key: CryptoKey, value: unknown) => seal(key, enc.encode(JSON.stringify(value)))
export const decryptJson = async <T,>(key: CryptoKey, payload: string): Promise<T> => JSON.parse(dec.decode(await open(key, payload))) as T
export const encryptText = async (key: CryptoKey, value: string) => seal(key, enc.encode(value))
export const decryptText = async (key: CryptoKey, payload: string) => dec.decode(await open(key, payload))

// ----- password tools -----
export function generatePassword(length = 18, opts = { upper: true, lower: true, digits: true, symbols: true }) {
  const sets = [opts.upper && 'ABCDEFGHJKLMNPQRSTUVWXYZ', opts.lower && 'abcdefghijkmnopqrstuvwxyz', opts.digits && '23456789', opts.symbols && '!@#$%^&*()-_=+[]{};:,.?'].filter(Boolean) as string[]
  if (sets.length === 0) return ''
  const all = sets.join('')
  const pick = (s: string) => s[crypto.getRandomValues(new Uint32Array(1))[0]! % s.length]
  const chars = sets.map(pick)
  while (chars.length < length) chars.push(pick(all))
  for (let i = chars.length - 1; i > 0; i--) { const j = crypto.getRandomValues(new Uint32Array(1))[0]! % (i + 1); [chars[i], chars[j]] = [chars[j]!, chars[i]!] }
  return chars.join('')
}

export function passwordStrength(pw: string): { score: number; label: string; color: string } {
  if (!pw) return { score: 0, label: '', color: '#94A3B8' }
  let pool = 0
  if (/[a-z]/.test(pw)) pool += 26
  if (/[A-Z]/.test(pw)) pool += 26
  if (/\d/.test(pw)) pool += 10
  if (/[^A-Za-z0-9]/.test(pw)) pool += 33
  let bits = pw.length * Math.log2(Math.max(pool, 1))
  if (/(.)\1{2,}/.test(pw) || /^(password|qwerty|123456|admin|welcome)/i.test(pw)) bits *= 0.5
  if (bits < 36) return { score: 1, label: 'Weak', color: '#EF4444' }
  if (bits < 60) return { score: 2, label: 'Fair', color: '#F59E0B' }
  if (bits < 80) return { score: 3, label: 'Good', color: '#10B981' }
  return { score: 4, label: 'Strong', color: '#059669' }
}
