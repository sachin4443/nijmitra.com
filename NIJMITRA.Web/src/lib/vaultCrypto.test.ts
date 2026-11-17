import { describe, expect, it } from 'vitest'
import { createVault, decryptJson, encryptJson, generatePassword, passwordStrength, rekeyVault, unlockVault } from './vaultCrypto'

describe('vault crypto', () => {
  it('encrypts so the server never sees plaintext and round-trips with the master password', async () => {
    const { request, key, recoveryKey } = await createVault('correct horse battery staple')
    const header = { kdfAlgorithm: 'PBKDF2-SHA256', ...request }
    const cipher = await encryptJson(key, { username: 'me@example.com', password: 'S3cret!' })
    expect(cipher).not.toContain('S3cret')
    expect(cipher.startsWith('v1.')).toBe(true)
    const again = await unlockVault('correct horse battery staple', header)
    expect(again).not.toBeNull()
    expect(await decryptJson<{ password: string }>(again!, cipher)).toEqual({ username: 'me@example.com', password: 'S3cret!' })
    expect(await unlockVault('wrong password', header)).toBeNull()
    // recovery key lets the user set a new master password; old data stays readable
    const rk = await rekeyVault(header, { recoveryKey }, 'new master pass')
    expect(rk).not.toBeNull()
    const newKey = await unlockVault('new master pass', { kdfAlgorithm: 'PBKDF2-SHA256', ...rk!.request })
    expect((await decryptJson<{ password: string }>(newKey!, cipher)).password).toBe('S3cret!')
  }, 60000)

  it('uses a fresh IV for every encryption', async () => {
    const { key } = await createVault('another master password')
    expect(await encryptJson(key, 'x')).not.toEqual(await encryptJson(key, 'x'))
  }, 30000)

  it('generates strong passwords', () => {
    const p = generatePassword(20)
    expect(p).toHaveLength(20)
    expect(passwordStrength(p).score).toBeGreaterThanOrEqual(3)
    expect(passwordStrength('123456').score).toBe(1)
  })
})
