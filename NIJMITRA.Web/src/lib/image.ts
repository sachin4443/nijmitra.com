/**
 * Shrinks large photos in the browser before upload (max 1920px, JPEG ~0.82).
 * Phone photos of 4–8 MB typically become 300–600 KB, which makes chat images send in a second or two.
 * Non-images, GIFs, SVGs and already-small files are returned unchanged.
 */
export async function compressImage(file: File, maxSide = 1920, quality = 0.82): Promise<File> {
  if (!/^image\/(jpeg|png|webp|heic|heif)$/i.test(file.type) || file.size < 350 * 1024) return file
  try {
    const bmp = await createImageBitmap(file, { imageOrientation: 'from-image' })
    const scale = Math.min(1, maxSide / Math.max(bmp.width, bmp.height))
    const w = Math.round(bmp.width * scale), h = Math.round(bmp.height * scale)
    const canvas = document.createElement('canvas')
    canvas.width = w; canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx) return file
    // PNGs with transparency keep a white background once converted to JPEG.
    ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, w, h)
    ctx.drawImage(bmp, 0, 0, w, h)
    bmp.close()
    const blob = await new Promise<Blob | null>((res) => canvas.toBlob(res, 'image/jpeg', quality))
    if (!blob || blob.size >= file.size) return file
    return new File([blob], file.name.replace(/\.[^.]+$/, '') + '.jpg', { type: 'image/jpeg', lastModified: Date.now() })
  } catch {
    return file
  }
}

/** Small in-memory cache of object URLs for authenticated images, so re-opening a chat doesn't download them again. */
const cache = new Map<string, string>()
export const imageCache = {
  get: (key: string) => cache.get(key),
  set: (key: string, url: string) => {
    cache.set(key, url)
    if (cache.size > 200) { const first = cache.keys().next().value as string; URL.revokeObjectURL(cache.get(first)!); cache.delete(first) }
  },
}
