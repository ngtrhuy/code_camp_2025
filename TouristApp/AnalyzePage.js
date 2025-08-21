// src/components/AnalyzePage.jsx
import React, { useEffect, useRef, useState } from "react";
import {
  fetchPreview,
  resolveSelection,
  validateConfig,
} from "../apis/analyzeApi";
import "../styles/analyze.css";

// helper: tính base href để các link tương đối hoạt động trong srcDoc
const computeBaseHref = (finalUrl, fallback) => {
  try {
    const u = new URL(finalUrl || fallback || '');
    // trả về thư mục chứa trang, ví dụ https://site.com/path/dir/
    return u.origin + u.pathname.replace(/[^/]*$/, '');
  } catch { return fallback || ''; }
};

// chèn <base href="..."> vào HTML srcDoc nếu thiếu
const injectBase = (html, baseHref) => {
  if (!html) return html;
  if (/<base\s/i.test(html)) return html;
  const tag = `<base href="${baseHref}">`;
  if (/<head[^>]*>/i.test(html)) return html.replace(/<head[^>]*>/i, m => `${m}${tag}`);
  return `<head>${tag}</head>${html}`;
};

const FIELDS = [
  { key: "TourListSelector", label: "TourListSelector", isContainer: true },
  { key: "TourName", label: "TourName" },
  { key: "TourPrice", label: "TourPrice" },
  { key: "TourCode", label: "TourCode" },
  { key: "ImageUrl", label: "ImageUrl (element)" },
  { key: "TourDetailUrl", label: "TourDetailUrl (element)" },
  { key: "DepartureLocation", label: "DepartureLocation" },
  { key: "DepartureDate", label: "DepartureDate (multiple)" },
  { key: "TourDuration", label: "TourDuration" },
];

const DETAIL_FIELDS = [
  { key: "TourDetailDayTitle", label: "TourDetailDayTitle" },
  { key: "TourDetailDayContent", label: "TourDetailDayContent" },
  { key: "TourDetailNote", label: "TourDetailNote" },
];

export default function AnalyzePage() {
  // --- fetch preview ---
  const [url, setUrl] = useState("https://www.bestprice.vn/tour");
  const [mode, setMode] = useState("client_side"); // server_side | client_side | auto
  const [loadMoreSelector, setLoadMoreSelector] = useState(".btn-more-tour");
  const [loadMoreClicks, setLoadMoreClicks] = useState(2);

  const [preview, setPreview] = useState({
    html: "",
    baseDomain: "",
    finalUrl: "",
    renderMode: "",
    logs: [],
  });
  const [loading, setLoading] = useState(false);
  const iframeRef = useRef(null);

  // Inspect + Preview navigation
  const [inspectTarget, setInspectTarget] = useState(null); // null | 'TourListSelector' | fieldKey
  const [navEnabled, setNavEnabled] = useState(true);
  const [historyStack, setHistoryStack] = useState([]); // [{url}]
  const [lastCss, setLastCss] = useState('');
  const [lastTag, setLastTag] = useState('');
  const [lastResolve, setLastResolve] = useState(null);
  const [error, setError] = useState('');

  // --- config draft ---
  const [config, setConfig] = useState({
    BaseDomain: "",
    BaseUrl: "",
    TourListSelector: "",
    TourName: "",
    TourPrice: "",
    TourCode: "",
    ImageUrl: "",
    ImageAttr: "src",
    TourDetailUrl: "",
    TourDetailAttr: "href",
    DepartureLocation: "",
    DepartureDate: "",
    TourDuration: "",
    CrawlType: "client_side",
    PagingType: "none",
    LoadMoreButtonSelector: "",
    LoadMoreType: "css",
    TourDetailDayTitle: "",
    TourDetailDayContent: "",
    TourDetailNote: "",
  });

  // Validate
  const [validateRes, setValidateRes] = useState(null);
  const [validating, setValidating] = useState(false);

  // fetch preview
  const ensureScheme = (u) => {
    if (!u) return u;
    const s = u.trim();
    if (s.startsWith("//")) return "https:" + s;
    if (!/^[a-zA-Z][a-zA-Z0-9+.\-]*:\/\//.test(s)) return "https://" + s;
    return s;
  };

  // Chuẩn hóa keys từ backend: camelCase ↔ PascalCase
  const normalizeResolve = (r = {}) => ({
    ...r,
    ItemAncestorXPath: r.ItemAncestorXPath ?? r.itemAncestorXPath ?? "",
    FieldRelativeXPath: r.FieldRelativeXPath ?? r.fieldRelativeXPath ?? "",
    AttrSuggestion: r.AttrSuggestion ?? r.attrSuggestion ?? {},
    ItemsMatched: r.ItemsMatched ?? r.itemsMatched ?? 0,
    FieldCoveragePct: r.FieldCoveragePct ?? r.fieldCoveragePct ?? 0,
    Samples: r.Samples ?? r.samples ?? [],
    Warnings: r.Warnings ?? r.warnings ?? [],
  });

  // Convert CSS selector thành XPath đơn giản
  const cssToSimpleXPath = (css, fieldKey) => {
    if (!css) return "";
    
    // Lấy selector cuối cùng (element được click)
    const parts = css.split(" > ");
    const lastPart = parts[parts.length - 1].trim();
    
    // Xác định prefix dựa trên field type
    const isAbsolute = fieldKey === "TourListSelector";
    let xpath = isAbsolute ? "//" : ".//";
    
    // Extract tag
    const tagMatch = lastPart.match(/^([a-zA-Z][a-zA-Z0-9]*)/);
    const tag = tagMatch ? tagMatch[1] : "*";
    xpath += tag;
    
    // Extract classes - sử dụng contains(@class,'...') cho ngắn gọn
    const classMatches = lastPart.match(/\.([a-zA-Z0-9_-]+)/g);
    if (classMatches && classMatches.length > 0) {
      const conditions = classMatches.map(cls => {
        const className = cls.substring(1); // remove leading dot
        return `contains(@class,'${className}')`;
      });
      xpath += `[${conditions.join(' and ')}]`;
    }
    
    return xpath;
  };

  const onFetch = async () => {
    setError('');
    setLoading(true);
    setInspectTarget(null);
    setLastCss('');
    setLastResolve(null);
    try {
      const safeUrl = ensureScheme(url); // 👈 thêm dòng này
      const res = await fetchPreview({
        url: safeUrl, // 👈 dùng URL đã normalize
        mode,
        loadMoreSelector,
        loadMoreClicks,
      });
      setPreview(res);
      setHistoryStack([]); // reset lịch sử khi fetch URL mới
      setConfig((c) => ({
        ...c,
        BaseDomain: res.baseDomain || '',
        BaseUrl: res.finalUrl || safeUrl,
        CrawlType: mode === 'server_side' ? 'server_side' : 'client_side',
        LoadMoreButtonSelector: mode === 'client_side' ? loadMoreSelector : '',
        LoadMoreType: mode === 'client_side' ? 'css' : c.LoadMoreType
      }));
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setLoading(false);
    }
  };

  // ---------------- Preview navigation helpers ----------------
  const openInPreview = async (href, { push = true } = {}) => {
    setError('');
    setLoading(true);
    try {
      const res = await fetchPreview({ 
        url: href, 
        mode,
        loadMoreSelector: mode === "client_side" ? loadMoreSelector : undefined,
        loadMoreClicks: mode === "client_side" ? loadMoreClicks : undefined,
      });
      if (push) {
        setHistoryStack((h) => [...h, { url: preview.finalUrl || url }]);
      }
      setPreview(res);
      setConfig((c) => ({
        ...c,
        BaseDomain: res.baseDomain || c.BaseDomain,
        BaseUrl: res.finalUrl || href
      }));
      setInspectTarget(null);
      setLastCss('');
      setLastTag('');
      setLastResolve(null);
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setLoading(false);
    }
  };

  const goBack = async () => {
    const prev = historyStack[historyStack.length - 1];
    if (!prev) return;
    setHistoryStack((h) => h.slice(0, -1));
    await openInPreview(prev.url, { push: false });
  };

  // ---------------- Inspector + navigation wiring ----------------
  useEffect(() => {
    const iframe = iframeRef.current;
    if (!iframe) return;
    const doc = iframe.contentDocument || iframe.contentWindow?.document;
    if (!doc) return;

    // overlay chỉ tạo khi đang inspect (không cần khi chỉ điều hướng)
    let overlay = null;
    if (inspectTarget) {
      overlay = doc.getElementById('__pickerOverlay');
      if (!overlay) {
        overlay = doc.createElement('div');
        overlay.id = '__pickerOverlay';
        Object.assign(overlay.style, {
          position: 'absolute',
          pointerEvents: 'none',
          border: '2px dashed #2f6feb',
          background: 'rgba(47, 111, 235, 0.08)',
          zIndex: 999999,
          display: 'none'
        });
        doc.body.appendChild(overlay);
      }
    }

    const toCssSelector = (el) => {
      if (!el || el.nodeType !== 1) return '';
      const parts = [];
      let node = el;
      let depth = 0;
      const MAX = 6;
      while (node && node.nodeType === 1 && depth < MAX) {
        let part = node.tagName.toLowerCase();
        const cls = (node.getAttribute('class') || '')
          .trim()
          .split(/\s+/)
          .filter((c) => c && !/^\d/.test(c) && !/^(ng\-|css\-|sc\-|chakra\-|Mui\-)/i.test(c))
          .slice(0, 2);
        if (cls.length) {
          part += '.' + cls.map((c) => c.replace(/([:.])/g, '\\$1')).join('.');
        } else if (node.parentElement) {
          const siblings = Array.from(node.parentElement.children).filter((ch) => ch.tagName === node.tagName);
          if (siblings.length > 1) {
            const idx = siblings.indexOf(node) + 1;
            part += `:nth-of-type(${idx})`;
          }
        }
        parts.unshift(part);
        node = node.parentElement;
        depth++;
      }
      return parts.join(' > ');
    };

    const move = (e) => {
      if (!inspectTarget || !overlay) return;
      const el = e.target;
      if (!el || el === overlay) return;
      const rect = el.getBoundingClientRect();
      const scrollTop = doc.documentElement.scrollTop || doc.body.scrollTop || 0;
      const scrollLeft = doc.documentElement.scrollLeft || doc.body.scrollLeft || 0;
      overlay.style.left = rect.left + scrollLeft + 'px';
      overlay.style.top = rect.top + scrollTop + 'px';
      overlay.style.width = rect.width + 'px';
      overlay.style.height = rect.height + 'px';
      overlay.style.display = 'block';
    };

         const click = async (e) => {
       const el = e.target;
       if (!el || el === overlay) return;
       
       // Nếu đang không inspect field nào và bật nav → điều hướng preview khi click <a>
       if (navEnabled && !inspectTarget) {
         const a = e.target?.closest && e.target.closest('a');
         const href = a && a.getAttribute && a.getAttribute('href');
         if (a && href && !/^javascript:/i.test(href)) {
           e.preventDefault();
           e.stopPropagation();
           const absolute = a.href || href;
           await openInPreview(absolute);
           return;
         }
       }

       // Inspect mode
       if (!inspectTarget) return;

       e.preventDefault();
       e.stopPropagation();

       const css = toCssSelector(el);
       setLastCss(css);
       setLastTag(el.tagName.toLowerCase());

       try {
         const payload = {
           url: preview.finalUrl || url,
           mode,
           loadMoreSelector: mode === 'client_side' ? loadMoreSelector : undefined,
           loadMoreClicks: mode === 'client_side' ? loadMoreClicks : undefined,
           selection: { css },
           ancestor:
             inspectTarget === 'TourListSelector'
               ? { auto: true }
               : config.TourListSelector
               ? { xpath: config.TourListSelector }
               : { auto: true },
           sampleLimit: 20
         };

         const raw = await resolveSelection(payload);
         const res = normalizeResolve(raw);
         setLastResolve(res);

         setConfig((c) => {
           const updated = { ...c };

           // 1) Gán TourListSelector chỉ khi đang inspect nó
           if (inspectTarget === 'TourListSelector') {
             const isGood = (s) => !!s && s.includes('[') && !/^\/\/(a|img|span|h\d)\b/i.test(s);
             if (isGood(res.ItemAncestorXPath)) {
               updated.TourListSelector = res.ItemAncestorXPath;
             }
           }

           // 2) Field đang inspect
           if (inspectTarget && inspectTarget !== 'TourListSelector') {
             if (res.FieldRelativeXPath && res.FieldRelativeXPath !== './/self::*') {
               updated[inspectTarget] = res.FieldRelativeXPath;

               // Gợi ý attr
               const imgAttr = res?.AttrSuggestion?.imageAttr;
               const linkAttr = res?.AttrSuggestion?.linkAttr;
               if (inspectTarget === 'ImageUrl' && imgAttr) updated.ImageAttr = imgAttr;
               if (inspectTarget === 'TourDetailUrl' && linkAttr) updated.TourDetailAttr = linkAttr;
             }
           }
           return updated;
         });
       } catch (err) {
         setError(String(err.message || err));
       } finally {
         setInspectTarget(null); // tắt inspect sau mỗi lần chọn
       }
    };

    const keydown = (e) => {
      if (e.key === 'Escape') setInspectTarget(null);
    };

    // Lắng nghe: mousemove (khi inspect), click (khi inspect hoặc nav), keydown
    if (inspectTarget) doc.addEventListener('mousemove', move, true);
    doc.addEventListener('click', click, true);
    doc.addEventListener('keydown', keydown, true);

    return () => {
      if (inspectTarget) doc.removeEventListener('mousemove', move, true);
      doc.removeEventListener('click', click, true);
      doc.removeEventListener('keydown', keydown, true);
      if (overlay && overlay.parentNode) overlay.parentNode.removeChild(overlay);
    };
  }, [
    inspectTarget,
    navEnabled,
    preview,
    mode,
    url,
    loadMoreClicks,
    loadMoreSelector,
    config.TourListSelector
  ]);

  const onValidate = async () => {
    setValidating(true);
    setError("");
    setValidateRes(null);
    try {
      const payload = {
        sampleLimit: 20,
        config,
      };
      const res = await validateConfig(payload);
      setValidateRes(res);
    } catch (e) {
      setError(String(e.message || e));
    } finally {
      setValidating(false);
    }
  };

  const coverage = validateRes?.perFieldCoverage || {};

  // ===== UI =================================================================
  const baseHref = computeBaseHref(preview.finalUrl || url, preview.baseDomain || '');
  const htmlWithBase = injectBase(preview.html, baseHref);

  return (
    <div className="analyze-wrapper">
      {/* LEFT PANEL */}
      <div className="panel">
        <h3>1 Fetch preview</h3>
        <div className="form-row">
          <label>URL</label>
          <input
            type="url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://..."
          />
        </div>
        <div className="form-row">
          <label>Render mode</label>
          <select value={mode} onChange={(e) => setMode(e.target.value)}>
            <option value="server_side">server_side</option>
            <option value="client_side">client_side</option>
            <option value="auto">auto</option>
          </select>
        </div>
        {mode !== "server_side" && (
          <>
            <div className="form-row">
              <label>Load more selector</label>
              <input
                type="text"
                value={loadMoreSelector}
                onChange={(e) => setLoadMoreSelector(e.target.value)}
                placeholder=".btn-more-tour or //xpath"
              />
            </div>
            <div className="form-row">
              <label>Load more clicks</label>
              <input
                type="text"
                value={loadMoreClicks}
                onChange={(e) => setLoadMoreClicks(Number(e.target.value || 0))}
              />
            </div>
          </>
        )}
        <div className="controls">
          <button onClick={onFetch} disabled={loading}>
            {loading ? "Loading..." : "Fetch"}
          </button>
          <span className="badge">render: {preview.renderMode || "-"}</span>
        </div>

        <div className="hr" />

                 <h3>2) Inspect & map fields</h3>
         <div className="controls">
           <button
             className="ghost"
             onClick={() => {
               setLastCss("");
               setLastResolve(null);
               setInspectOn(false);
               setCurrentField("");
             }}
           >
             Clear selection
           </button>
           {inspectOn && (
             <span className="badge">
               Inspecting: {currentField || "None"} (Press Esc to stop)
             </span>
           )}
         </div>
         <div className="info-box" style={{ marginTop: 8, marginBottom: 8, padding: 8, background: "#f8f9fa", borderRadius: 6, fontSize: 12 }}>
           <div><strong>Hướng dẫn:</strong> Click vào phần tử tương ứng trên trang để tự động tạo XPath selector. Khi không inspect, click vào tour link sẽ mở detail page.</div>
         </div>
        <div style={{ marginTop: 8 }}>
          <div className="kv">
            <div className="key">Last tag</div>
            <div className="val mono">{lastTag || "-"}</div>
            <div className="key">Selected CSS</div>
            <div className="val mono">{lastCss || "-"}</div>
          </div>
        </div>

        {lastResolve && (
          <div style={{ marginTop: 10 }}>
            <div className="kv">
              <div className="key">ItemsMatched</div>
              <div className="val">{lastResolve.ItemsMatched}</div>
              <div className="key">FieldCoverage</div>
              <div className="val">{lastResolve.FieldCoveragePct}%</div>
              {lastResolve?.AttrSuggestion?.imageAttr && (
                <>
                  <div className="key">Suggested imageAttr</div>
                  <div className="val mono">{lastResolve.AttrSuggestion.imageAttr}</div>
                </>
              )}
              {lastResolve?.AttrSuggestion?.linkAttr && (
                <>
                  <div className="key">Suggested linkAttr</div>
                  <div className="val mono">{lastResolve.AttrSuggestion.linkAttr}</div>
                </>
              )}
            </div>
            {lastResolve.Warnings?.length > 0 && (
              <ul style={{ marginTop: 6 }}>
                {lastResolve.Warnings.map((w, i) => (
                  <li key={i} className="warn">
                    ⚠ {w}
                  </li>
                ))}
              </ul>
            )}
            <div className="samples" style={{ marginTop: 8 }}>
              <div className="small">Samples ({lastResolve.Samples?.length || 0})</div>
              <ul>
                {(lastResolve.Samples || []).slice(0, 5).map((s, i) => (
                  <li key={i} className="mono">
                    {s}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        )}

        <div className="hr" />

        <h3>3) Draft config (auto-filled)</h3>
        <div className="mapping-grid">
          <div className="mapping-item">
            <div>BaseDomain</div>
            <div className="mono">{config.BaseDomain}</div>
          </div>
          <div className="mapping-item">
            <div>BaseUrl</div>
            <div className="mono">{config.BaseUrl}</div>
          </div>
          
                     {/* All fields với inspect buttons */}
           {[...FIELDS, ...DETAIL_FIELDS].map((f) => (
             <div key={f.key} className="mapping-item-with-inspect">
               <div className="field-label">{f.label}</div>
               <div className="field-input-group">
                 <input
                   type="text"
                   value={config[f.key] || ""}
                   onChange={(e) =>
                     setConfig((c) => ({ ...c, [f.key]: e.target.value }))
                   }
                   placeholder="XPath selector"
                   className="field-input"
                 />
                 <button
                   className={`inspect-btn ${inspectOn && currentField === f.key ? "active" : ""}`}
                   onClick={() => {
                     if (inspectOn && currentField === f.key) {
                       setInspectOn(false);
                       setCurrentField("");
                     } else {
                       setCurrentField(f.key);
                       setInspectOn(true);
                     }
                   }}
                   disabled={!preview.html}
                   title={`Inspect ${f.label}`}
                 >
                   🎯
                 </button>
               </div>
             </div>
           ))}
          
          <div className="mapping-item">
            <div>ImageAttr</div>
            <div>
              <input
                type="text"
                value={config.ImageAttr}
                onChange={(e) =>
                  setConfig((c) => ({ ...c, ImageAttr: e.target.value }))
                }
              />
            </div>
          </div>
          <div className="mapping-item">
            <div>TourDetailAttr</div>
            <div>
              <input
                type="text"
                value={config.TourDetailAttr}
                onChange={(e) =>
                  setConfig((c) => ({ ...c, TourDetailAttr: e.target.value }))
                }
              />
            </div>
          </div>

          <div className="mapping-item">
            <div>PagingType</div>
            <div>
              <select
                value={config.PagingType}
                onChange={(e) =>
                  setConfig((c) => ({ ...c, PagingType: e.target.value }))
                }
              >
                <option value="none">none</option>
                <option value="querystring">querystring</option>
                <option value="load_more">load_more</option>
                <option value="carousel">carousel</option>
              </select>
            </div>
          </div>
          {config.PagingType === "load_more" && (
            <>
              <div className="mapping-item">
                <div>LoadMoreButtonSelector</div>
                <div>
                  <input
                    type="text"
                    value={config.LoadMoreButtonSelector}
                    onChange={(e) =>
                      setConfig((c) => ({
                        ...c,
                        LoadMoreButtonSelector: e.target.value,
                      }))
                    }
                  />
                </div>
              </div>
              <div className="mapping-item">
                <div>LoadMoreType</div>
                <div>
                  <select
                    value={config.LoadMoreType}
                    onChange={(e) =>
                      setConfig((c) => ({ ...c, LoadMoreType: e.target.value }))
                    }
                  >
                    <option value="css">css</option>
                    <option value="xpath">xpath</option>
                    <option value="class">class</option>
                  </select>
                </div>
              </div>
            </>
          )}
        </div>

        <div className="hr" />

        <h3>4) Validate</h3>
        <div className="controls">
          <button
            onClick={onValidate}
            disabled={validating || !config.TourListSelector}
          >
            {validating ? "Validating..." : "Validate config"}
          </button>
          <button className="secondary" onClick={() => setValidateRes(null)}>
            Clear result
          </button>
        </div>
        {error && (
          <div className="warn" style={{ marginTop: 8 }}>
            ⚠ {error}
          </div>
        )}

        {validateRes && (
          <div style={{ marginTop: 10 }}>
            <div className="ok">{validateRes.message}</div>
            <div className="kv" style={{ marginTop: 8 }}>
              <div className="key">ItemsFound</div>
              <div className="val">{validateRes.itemsFound}</div>
            </div>
            <div className="hr" />
            <div className="small">Coverage</div>
            <table className="table">
              <thead>
                <tr>
                  <th>Field</th>
                  <th>%</th>
                </tr>
              </thead>
              <tbody>
                {Object.keys(coverageList).map((k) => (
                  <tr key={k}>
                    <td className="mono">{k}</td>
                    <td>{Math.round((coverageList[k] || 0) * 100) / 100}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {validateRes.warnings?.length > 0 && (
              <>
                <div className="hr" />
                <div className="small">Warnings</div>
                <ul>
                  {validateRes.warnings.map((w, i) => (
                    <li key={i} className="warn">
                      ⚠ {w}
                    </li>
                  ))}
                </ul>
              </>
            )}
            <div className="hr" />
            <div className="small">Samples</div>
            <div className="samples">
              <pre className="mono" style={{ whiteSpace: "pre-wrap" }}>
                {JSON.stringify(validateRes.samples, null, 2)}
              </pre>
            </div>
          </div>
        )}
      </div>

      {/* RIGHT: PREVIEW */}
      <div className="panel">
                 <h3>Preview</h3>
         <div className="controls" style={{ marginBottom: 8 }}>
           <span className="badge">finalUrl: {preview.finalUrl || "-"}</span>
           <span className="badge">baseDomain: {preview.baseDomain || "-"}</span>
           <label className="badge" style={{ cursor: "pointer" }}>
             <input 
               type="checkbox" 
               checked={navEnabled} 
               onChange={e => setNavEnabled(e.target.checked)} 
               style={{ marginRight: 6 }} 
             />
             open links in preview
           </label>
           <button className="secondary" onClick={goBack} disabled={!historyStack.length}>
             Back ({historyStack.length})
           </button>
         </div>
         <div className="iframe-wrap">
           {preview.html ? (
             (() => {
               const baseHref = computeBaseHref(preview.finalUrl || url, preview.baseDomain || '');
               const htmlWithBase = injectBase(preview.html, baseHref);
               return (
                 <iframe
                   ref={iframeRef}
                   className="preview-iframe"
                   title="preview"
                   srcDoc={htmlWithBase}
                   sandbox="allow-same-origin allow-scripts allow-forms"
                 />
               );
             })()
           ) : (
             <div className="small" style={{ padding: 10, color: "#aaa" }}>
               Chưa có preview. Nhấn <b>Fetch</b> để tải HTML và hiển thị trang.
             </div>
           )}
        </div>
        {!!preview.logs?.length && (
          <>
            <div className="hr" />
            <div className="small">Logs</div>
            <ul>
              {preview.logs.map((l, i) => (
                <li key={i} className="mono">
                  {l}
                </li>
              ))}
            </ul>
          </>
        )}
      </div>
    </div>
  );
}
