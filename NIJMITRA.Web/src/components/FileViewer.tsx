import { useEffect, useRef, useState } from 'react'
import { Alert, Button, ButtonGroup, Nav, Spinner, Table } from 'react-bootstrap'
import { FiMaximize, FiMinus, FiPlus } from 'react-icons/fi'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { api, apiError } from '../lib/api'

/* ------------------------------------------------------------------ PDF (pdf.js — works on Android/iOS where <iframe> PDFs don't) */
const PDF_ASSETS = import.meta.env.DEV ? '/node_modules/pdfjs-dist/' : '/pdfjs/'

async function loadPdfJs() {
  const [lib, worker] = await Promise.all([import('pdfjs-dist'), import('pdfjs-dist/build/pdf.worker.min.mjs?url')])
  lib.GlobalWorkerOptions.workerSrc = worker.default
  return lib
}

function PdfPage({ pdf, n, width, zoom }: { pdf: PDFDocumentProxy; n: number; width: number; zoom: number }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const holder = useRef<HTMLDivElement>(null)
  const [visible, setVisible] = useState(n <= 2)
  const [size, setSize] = useState<{ w: number; h: number } | null>(null)
  // Render pages only when they scroll near the screen — large PDFs stay fast on phones.
  useEffect(() => {
    const el = holder.current
    if (!el || visible) return
    const io = new IntersectionObserver((e) => { if (e.some((x) => x.isIntersecting)) setVisible(true) }, { rootMargin: '800px 0px' })
    io.observe(el)
    return () => io.disconnect()
  }, [visible])
  useEffect(() => {
    let cancelled = false
    let task: { cancel: () => void; promise: Promise<void> } | null = null
    void pdf.getPage(n).then((page) => {
      if (cancelled) return
      const base = page.getViewport({ scale: 1 })
      const scale = (width / base.width) * zoom
      const vp = page.getViewport({ scale })
      setSize({ w: vp.width, h: vp.height })
      if (!visible || !canvas.current) return
      const dpr = Math.min(window.devicePixelRatio || 1, 2.5)
      const c = canvas.current
      c.width = Math.floor(vp.width * dpr); c.height = Math.floor(vp.height * dpr)
      c.style.width = `${vp.width}px`; c.style.height = `${vp.height}px`
      const ctx = c.getContext('2d')
      if (!ctx) return
      task = page.render({ canvasContext: ctx, viewport: vp, transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : undefined })
      task.promise.catch(() => {})
    })
    return () => { cancelled = true; task?.cancel() }
  }, [pdf, n, width, zoom, visible])
  return (
    <div ref={holder} className="pdf-page" style={size ? { width: size.w, height: size.h } : { width: width * zoom, height: width * zoom * 1.414 }}>
      {visible ? <canvas ref={canvas} /> : null}
      <span className="pdf-page-no">{n}</span>
    </div>
  )
}

export function PdfViewer({ url }: { url: string }) {
  const box = useRef<HTMLDivElement>(null)
  const [pdf, setPdf] = useState<PDFDocumentProxy | null>(null)
  const [error, setError] = useState('')
  const [width, setWidth] = useState(800)
  const [zoom, setZoom] = useState(1)
  useEffect(() => {
    let doc: PDFDocumentProxy | null = null
    let cancelled = false
    setPdf(null); setError('')
    Promise.all([loadPdfJs(), api.get<ArrayBuffer>(url, { responseType: 'arraybuffer' })])
      .then(([lib, r]) => lib.getDocument({ data: new Uint8Array(r.data), isEvalSupported: false, cMapUrl: `${PDF_ASSETS}cmaps/`, cMapPacked: true, standardFontDataUrl: `${PDF_ASSETS}standard_fonts/` }).promise)
      .then((d) => { if (cancelled) void d.destroy(); else { doc = d; setPdf(d) } })
      .catch((e: { name?: string }) => { if (!cancelled) setError(e?.name === 'PasswordException' ? 'This PDF is password-protected. Download it to open with its password.' : 'This PDF could not be displayed. You can still download it.') })
    return () => { cancelled = true; void doc?.destroy() }
  }, [url])
  useEffect(() => {
    const el = box.current
    if (!el) return
    const ro = new ResizeObserver(() => setWidth(Math.max(240, Math.min(el.clientWidth - 24, 1100))))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])
  return (
    <div className="pdf-viewer">
      <div className="pdf-toolbar">
        <span className="small text-muted">{pdf ? `${pdf.numPages} page${pdf.numPages === 1 ? '' : 's'}` : ''}</span>
        <ButtonGroup size="sm">
          <Button variant="outline-secondary" onClick={() => setZoom((z) => Math.max(0.5, +(z - 0.25).toFixed(2)))} aria-label="Zoom out"><FiMinus /></Button>
          <Button variant="outline-secondary" onClick={() => setZoom(1)} aria-label="Fit width"><FiMaximize /> {Math.round(zoom * 100)}%</Button>
          <Button variant="outline-secondary" onClick={() => setZoom((z) => Math.min(3, +(z + 0.25).toFixed(2)))} aria-label="Zoom in"><FiPlus /></Button>
        </ButtonGroup>
      </div>
      <div className="pdf-scroll" ref={box}>
        {error && <Alert variant="warning" className="m-3">{error}</Alert>}
        {!pdf && !error && <div className="text-center p-5"><Spinner /></div>}
        {pdf && Array.from({ length: pdf.numPages }, (_, i) => <PdfPage key={i} pdf={pdf} n={i + 1} width={width} zoom={zoom} />)}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ Excel / CSV / Word (server converts to plain rows & text) */
interface Preview { kind: 'table' | 'document'; truncated: boolean; sheets?: { name: string; rows: string[][]; truncated: boolean }[]; blocks?: { type: string; text?: string; rows?: string[][] }[] }

export function OfficeViewer({ url }: { url: string }) {
  const [data, setData] = useState<Preview | null>(null)
  const [error, setError] = useState('')
  const [sheet, setSheet] = useState(0)
  useEffect(() => {
    setData(null); setError(''); setSheet(0)
    api.get<Preview>(url + (url.includes('?') ? '&' : '?') + 'view=preview').then((r) => setData(r.data)).catch((e) => setError(apiError(e).message))
  }, [url])
  if (error) return <Alert variant="warning" className="m-3">{error}</Alert>
  if (!data) return <div className="text-center p-5"><Spinner /></div>
  if (data.kind === 'table') {
    const s = data.sheets?.[sheet]
    return (
      <div className="office-viewer">
        {(data.sheets?.length ?? 0) > 1 && <Nav variant="tabs" activeKey={sheet} onSelect={(k) => setSheet(Number(k))} className="px-2 pt-2 flex-nowrap overflow-auto">
          {data.sheets!.map((x, i) => <Nav.Item key={i}><Nav.Link eventKey={i} className="text-nowrap">{x.name}</Nav.Link></Nav.Item>)}</Nav>}
        {!s || s.rows.length === 0 ? <div className="p-4 text-muted">This sheet is empty.</div> : (
          <div className="office-table-wrap">
            <Table bordered size="sm" className="office-table mb-0">
              <thead><tr><th className="rn">#</th>{s.rows[0].map((c, i) => <th key={i}>{c}</th>)}</tr></thead>
              <tbody>{s.rows.slice(1).map((r, i) => <tr key={i}><td className="rn">{i + 2}</td>{r.map((c, j) => <td key={j}>{c}</td>)}</tr>)}</tbody>
            </Table>
          </div>)}
        {(s?.truncated || data.truncated) && <div className="small text-muted p-2">Showing the first part of this file. Download it to see everything.</div>}
      </div>
    )
  }
  return (
    <div className="office-doc">
      {(data.blocks ?? []).length === 0 && <p className="text-muted">This document has no text to show.</p>}
      {(data.blocks ?? []).map((b, i) => b.type === 'table'
        ? <div key={i} className="office-table-wrap mb-3"><Table bordered size="sm" className="office-table mb-0"><tbody>{b.rows!.map((r, j) => <tr key={j}>{r.map((c, k) => <td key={k}>{c}</td>)}</tr>)}</tbody></Table></div>
        : b.type === 'h1' ? <h3 key={i}>{b.text}</h3> : b.type === 'h2' ? <h4 key={i}>{b.text}</h4> : b.type === 'h3' ? <h5 key={i}>{b.text}</h5>
        : b.type === 'li' ? <div key={i} className="office-li">• {b.text}</div> : <p key={i}>{b.text}</p>)}
      {data.truncated && <div className="small text-muted">Showing the first part of this document. Download it to see everything.</div>}
      <div className="small text-muted mt-4 border-top pt-2">Text preview — formatting and images are not shown. Download the file to see the original layout.</div>
    </div>
  )
}
