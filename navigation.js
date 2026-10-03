(() => {
  'use strict';
  const VERSION = '1.0.0';
  const BUILD = '20261003-manual';
  if (window.__chatRail?.version === VERSION && window.__chatRail.build === BUILD) return { installed: true, version: VERSION };
  window.__chatRail?.stop?.();
  const adapter = window.__CHAT_RAIL_ADAPTER__;
  if (!adapter || (!window.__CHAT_RAIL_TEST__ && window.electronBridge?.windowType !== 'electron'))
    return { installed: false, reason: 'not-main-desktop-renderer' };
  let loaderPattern = null;
  try { if(adapter.historyLoaderPattern) loaderPattern = new RegExp(adapter.historyLoaderPattern); } catch {}
  let clientPromise = null;
  async function historyClient() {
    if(window.__CHAT_RAIL_TEST__ && window.__CHAT_RAIL_TEST_CLIENT__) return window.__CHAT_RAIL_TEST_CLIENT__[adapter.export];
    if(clientPromise) return clientPromise;
    clientPromise=(async() => {
      for(const candidate of adapter.clientCandidates || [adapter]) {
        try { const module=await import(new URL(candidate.module,document.baseURI).href); const client=module[candidate.export];
          if(typeof client?.safeGet==='function') return client;
        } catch {}
      }
      // Inspect only static packaged code, never authentication or account storage.
      for(const file of adapter.clientProbes || []) {
        const response=await fetch(new URL(file,document.baseURI)); if(!response.ok) continue;
        const source=await response.text();
        const calls=[...source.matchAll(/([$A-Za-z_][\w$]*)\.safeGet\(\s*[`"]\/conversation\/\{conversation_id\}/g)].map(m=>m[1]);
        for(const entry of source.matchAll(/import\{([^}]+)\}from"(\.\/app-shared-[a-f0-9]+\.js)"/g)) {
          for(const binding of entry[1].split(',')) {
            const names=binding.trim().split(/\s+as\s+/); if(!calls.includes(names[1] || names[0])) continue;
            const module=await import(new URL(entry[2],new URL(file,document.baseURI)).href);
            const client=module[names[0]]; if(typeof client?.safeGet==='function') return client;
          }
        }
      }
      throw new Error('当前版本的历史接口尚未识别，请重新启动导航工具以适配当前版本');
    })();
    try { return await clientPromise; } catch(e) {clientPromise=null;throw e;}
  }

  function activeBranch(conversation) {
    const mapping = conversation?.mapping;
    if (!mapping || !conversation.current_node || !mapping[conversation.current_node])
      throw new Error('完整对话数据格式与适配器不匹配');
    const chain = [], seen = new Set();
    let id = conversation.current_node;
    while (id != null) {
      if (seen.has(id)) throw new Error('历史消息链包含循环');
      seen.add(id);
      const node = mapping[id];
      if (!node) throw new Error('历史消息链不完整');
      if (node.message?.author?.role === 'user' && !node.message.metadata?.is_visually_hidden_from_conversation) {
        const m = node.message;
        const parts = m.content?.parts || [];
        const text = parts.filter(p => typeof p === 'string').join('\n').trim();
        chain.push({ id: m.id || id, text: text || '图片或附件消息', attachment: !text, sentAt: messageTime(m.create_time) });
      }
      id = node.parent;
    }
    return chain.reverse();
  }
  let fiberAnchor = null;
  function rootFiber() {
    const shown = el => { const r=el.getBoundingClientRect(); return r.width>0 && r.height>0 && el.checkVisibility?.({checkVisibilityCSS:true})!==false; };
    if (!fiberAnchor?.isConnected || !shown(fiberAnchor)) fiberAnchor=null;
    if (!fiberAnchor) for(const el of document.querySelectorAll('[data-chatgpt-search-message-ids], [data-chatgpt-search-unit-key]')) if(shown(el)){fiberAnchor=el;break;}
    const anchor = fiberAnchor;
    if (!anchor) return null;
    let el = anchor, fiber;
    while (el && !fiber) {
      const key = Object.keys(el).find(k => k.startsWith('__reactFiber$'));
      if (key) fiber = el[key];
      el = el.parentElement;
    }
    if (!fiber) return null;
    let top = fiber;
    while (top.return) top = top.return;
    if (top.stateNode?.current && top.stateNode.current !== top && fiber.alternate) fiber = fiber.alternate;
    return { fiber, anchor };
  }
  function nativeState() {
    const location = rootFiber();
    if (!location) return null;
    let best = null, api = null, ensureHistory = null, open = null, chat = false;
    // Follow ONLY this visible message's ancestry, never the application tree:
    // Activity keeps prior conversation trees mounted while they are hidden.
    for (let f = location.fiber, count = 0; f && count++ < 100; f = f.return) {
      const p = f.memoizedProps;
      if (!p || typeof p !== 'object') continue;
      if (p.isWorkConversation === true || p.isChatConversation === false) return null;
      if (p.isChatConversation === true) chat = true;
      if (Array.isArray(p.entries) && p.conversationId && p.entries.some(e => Array.isArray(e.turn?.items))) {
        if (best && best.conversationId !== p.conversationId) break;
        best = p;
        if (p.turnListApi?.scrollToKey) api = p.turnListApi;
        if (p.ensureHistory) ensureHistory = p.ensureHistory;
        const values = f.updateQueue?.memoCache?.data?.flat(2) || [];
        for (let h = f.memoizedState, n = 0; h && n++ < 150; h = h.next) {
          values.push(h.memoizedState, h.memoizedState?.current);
        }
        for (const value of values) {
          if (value?.scrollToKey) api = value;
          if (value?.ensureHistory) ensureHistory = value.ensureHistory;
          if (typeof value === 'function' && loaderPattern &&
              loaderPattern.test(Function.prototype.toString.call(value))) ensureHistory = value;
        }
      }
      if (p.ref?.current?.scrollToMessage) open = p.ref.current.scrollToMessage;
    }
    if (!best || (!chat && !window.__CHAT_RAIL_TEST__)) return null;
    return { id: String(best.conversationId), entries: best.entries, api, open, ensureHistory, anchor: location.anchor };
  }
  function messagesFromEntries(entries) {
    return entries.flatMap(e => (e.turn?.items || []).filter(i => i.type === 'user-message').map(i => ({
      id: i.messageId || i.id || e.turn.messageIds?.[0], text: typeof i.message==='string' && i.message ? i.message : '图片或附件消息'
    }))).filter(m => m.id);
  }
  function messageTime(value) {
    if(value==null || value==='' || !['number','string'].includes(typeof value)) return null;
    const number=Number(value);
    if(!Number.isFinite(number) || number<=0) return null;
    const milliseconds=number<1e12 ? number*1000 : number;
    return Number.isFinite(new Date(milliseconds).getTime()) ? milliseconds : null;
  }
  function formatMessageTime(value) {
    if(!value) return '';
    const date=new Date(value), pad=n=>String(n).padStart(2,'0');
    return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }
  const host = document.createElement('div');
  host.id = 'local-chat-navigation-rail';
  const shadow = host.attachShadow({ mode: 'open' });
  shadow.innerHTML = `<style>
    :host{--ink:#ececf1;--muted:#767680;--card:rgba(34,34,38,.97);--stroke:rgba(255,255,255,.12);--active:#b9bac5;--ease-out:cubic-bezier(.23,1,.32,1);position:fixed;right:18px;top:50%;transform:translateY(-50%);z-index:2147483000;width:36px;max-height:70vh;font-family:system-ui,'Segoe UI','Microsoft YaHei',sans-serif;color:var(--ink);color-scheme:dark;user-select:none}
    :host([data-theme=light]){--ink:#242429;--muted:#a2a2ac;--card:rgba(255,255,255,.98);--stroke:rgba(0,0,0,.10);--active:#565660;color-scheme:light}
    *{box-sizing:border-box}button{font:inherit;color:inherit}#rail{display:flex;flex-direction:column;align-items:center;max-height:70vh;overflow-y:auto;overscroll-behavior:contain;scrollbar-width:none;padding:8px 0;gap:0}#rail::-webkit-scrollbar{display:none}
    .item{position:relative;display:flex;flex:0 0 10px;width:36px;height:10px;align-items:center;justify-content:flex-end;border:0;background:transparent;cursor:pointer;padding:0 5px;outline:none}
    .item[data-bookmarked=true]::after{content:'';position:absolute;right:1px;top:4px;width:2px;height:2px;border-radius:50%;background:var(--color-codex-description,var(--muted))}
    .bar{display:block;width:26px;height:2px;background:var(--color-codex-description,var(--muted));opacity:.4;transform:scaleX(.2308);transform-origin:right center;transition:transform 160ms var(--ease-out),opacity 125ms ease,background-color 125ms ease}
    .item[data-active=true] .bar{opacity:.6;background:var(--ink)}
    .item[data-bookmarked=true] .bar{opacity:1}
    .item[data-active=true]::after,.item:hover::after,.item:focus-visible::after{background:var(--ink)}
    .item:focus-visible{outline:2px solid var(--active);outline-offset:-2px;border-radius:5px}.item:focus-visible .bar{opacity:1}
    .item:focus-visible .bar{transform:scaleX(1);opacity:1;background:var(--ink);transition:none}
    @media(hover:hover) and (pointer:fine){
      .item:hover .bar{transform:scaleX(1);opacity:1;background:var(--ink)}
      .item:has(+.item:hover) .bar,.item:hover+.item .bar{transform:scaleX(.7692)}
      .item:has(+.item+.item:hover) .bar,.item:hover+.item+.item .bar{transform:scaleX(.5385)}
      .item:has(+.item+.item+.item:hover) .bar,.item:hover+.item+.item+.item .bar{transform:scaleX(.3846)}
    }
    .item:active .bar{opacity:.8}
    :host{--ink:var(--color-text-primary,#ececf1);--card:var(--color-surface-elevated-secondary,#222226);--stroke:var(--color-border-primary-outline,rgba(255,255,255,.12))}
    :host([data-theme=light]){--ink:var(--color-text-primary,#242429);--card:var(--color-surface-elevated-secondary,#fff);--stroke:var(--color-border-primary-outline,rgba(0,0,0,.10))}
    #scope:empty{display:none}
    #preview{position:absolute;right:42px;width:min(320px,calc(100vw - 100px));padding:12px 14px;border:1px solid var(--stroke);border-radius:8px;background:var(--card);box-shadow:0 4px 16px rgba(0,0,0,.10);opacity:0;visibility:hidden;transform:translateX(3px) scale(.98);transform-origin:right center;transition:opacity 125ms var(--ease-out),transform 125ms var(--ease-out),visibility 125ms;pointer-events:none}
    #preview[data-open=true]{opacity:1;visibility:visible;transform:translateX(0) scale(1);pointer-events:auto}
    #preview[data-keyboard=true]{transition:none}
    .meta{display:flex;align-items:center;gap:9px;margin-bottom:8px;color:var(--color-text-tertiary,var(--muted));font-size:11px}.badge{color:var(--color-text-tertiary,var(--muted));font-variant-numeric:tabular-nums}
    #bookmark{display:flex;align-items:center;justify-content:center;flex:none;width:24px;height:24px;margin-left:auto;padding:4px;border:0;border-radius:4px;background:transparent;color:var(--color-text-tertiary,var(--muted));cursor:pointer}
    #bookmark[aria-pressed=true]{color:var(--ink)}#bookmark:hover{background:var(--color-background-hover,rgba(127,127,127,.10));color:var(--ink)}#bookmark:focus-visible{outline:2px solid var(--active);outline-offset:1px}#bookmark svg{width:16px;height:16px}
    #text{font-size:13px;line-height:1.7;white-space:pre-wrap;overflow-wrap:anywhere;display:-webkit-box;-webkit-line-clamp:9;-webkit-box-orient:vertical;overflow:hidden;color:var(--ink)}
    #notice:empty{display:none}#notice{margin-top:8px;font-size:11px;line-height:1.5;color:var(--color-text-tertiary,var(--muted))}
    #empty{width:36px;height:24px;text-align:center;color:var(--muted);font-size:17px;cursor:help}
    @media(prefers-reduced-motion:reduce){.bar{transition:none}#preview,#preview[data-open=true]{transform:none;transition:opacity 125ms ease,visibility 125ms}}
  </style><nav id="rail" aria-label="当前对话的用户消息"></nav><div id="preview" role="dialog" aria-label="消息预览"><div class="meta"><span class="badge" id="number"></span><span id="scope"></span><button id="bookmark" type="button" aria-label="收藏此轮对话" aria-pressed="false"></button></div><div id="text"></div><div id="notice" aria-live="polite"></div></div>`;
  const history = document.createElement('div'); history.id='history'; history.setAttribute('role','status');
  const previewElement=shadow.getElementById('preview'); previewElement.insertBefore(history,shadow.getElementById('notice'));
  const loadingStyle=document.createElement('style');loadingStyle.textContent='#history{margin-top:8px;font-size:11px;line-height:1.5;color:var(--color-text-tertiary,var(--muted))}#history:empty{display:none}';shadow.append(loadingStyle);
  document.body.append(host);
  const rail = shadow.getElementById('rail'), card = shadow.getElementById('preview');
  const text = shadow.getElementById('text'), number = shadow.getElementById('number');
  const scope = shadow.getElementById('scope'), notice = shadow.getElementById('notice');
  const bookmark = shadow.getElementById('bookmark');
  const bookmarkKey='local-chat-rail.bookmarks.v1';
  let bookmarks = Object.create(null);
  try { const value=JSON.parse(localStorage.getItem(bookmarkKey) || '{}');
    if(value.v===1 && value.conversations && typeof value.conversations==='object')
      for(const [id,ids] of Object.entries(value.conversations))
        if(id.length<512 && Array.isArray(ids)) bookmarks[id]=new Set(ids.filter(x=>typeof x==='string' && x.length<512));
  } catch {}
  const bookmarkOutline='<path d="M6.98828 11.7607C7.6143 11.3998 8.3857 11.3998 9.01172 11.7607L11.0127 12.915C11.6627 13.2898 12.4746 12.8206 12.4746 12.0703V4.7998C12.4745 3.54351 11.4565 2.5255 10.2002 2.52539H5.7998C4.54351 2.5255 3.5255 3.54351 3.52539 4.7998V12.0703C3.52539 12.8206 4.33731 13.2898 4.9873 12.915L6.98828 11.7607ZM13.5254 12.0703C13.5254 13.6286 11.8383 14.6026 10.4883 13.8242L8.4873 12.6709C8.18589 12.4971 7.81411 12.4971 7.5127 12.6709L5.51172 13.8242C4.16172 14.6026 2.47461 13.6286 2.47461 12.0703V4.7998C2.47472 2.96361 3.96361 1.47472 5.7998 1.47461H10.2002C12.0364 1.47472 13.5253 2.96361 13.5254 4.7998V12.0703Z" fill="currentColor"/>';
  const bookmarkFill='<path d="M10.2002 1.47461C12.0364 1.47472 13.5253 2.96361 13.5254 4.7998V12.0703C13.5254 13.6286 11.8383 14.6026 10.4883 13.8242L8.4873 12.6709C8.18589 12.4971 7.81411 12.4971 7.5127 12.6709L5.51172 13.8242C4.16172 14.6026 2.47461 13.6286 2.47461 12.0703V4.7998C2.47472 2.96361 3.96361 1.47472 5.7998 1.47461H10.2002Z" fill="currentColor"/>';
  function isBookmarked(id) {return bookmarks[current]?.has(id)===true;}
  function updateBookmark() {
    const m=messages[hovered], active=m && isBookmarked(m.id);
    bookmark.disabled=!m; bookmark.setAttribute('aria-pressed',String(!!active));
    bookmark.setAttribute('aria-label',active?'移除收藏':'收藏此轮对话'); bookmark.title=active?'移除收藏':'收藏此轮对话';
    bookmark.innerHTML='<svg viewBox="0 0 16 16" aria-hidden="true">'+(active?bookmarkFill:bookmarkOutline)+'</svg>';
  }
  let stopped = false, current = '', messages = [], complete = false, busy = false;
  let error = '', generation = 0, timer, delay, dismiss, hovered = -1, lastFetch = 0, lastSuccess = 0;
  const controllers = new Set();
  const cache = new Map(), loading = new Map();
  const metrics = { domPasses: 0, entryPasses: 0, activeWrites: 0, historyRequests: 0, cacheHits: 0, nativeLoads: 0, lastJumpMs: 0, lastHistoryMs: 0 };
  let messageIndex = new Map(), nodeIndex = new Map(), domDirty = true, frame = 0;
  let entryIndex = null, entryArray = null, entryRevision = -1, nativeRevision = 0;
  let jumpSequence = 0, jumpAbort = null, fetching = '', failures = 0;
  let loadedEntries = null, loadedRevision = -1, loadedMessages = [], activeButton = null;
  const buttons = new Map();
  function updateHistoryIndicator() {history.textContent=current && !error && (!complete || loading.has(generation+':'+current)) ? '历史读取中' : '';}
  function entryMessages(state) {
    if(loadedEntries !== state.entries || loadedRevision !== nativeRevision) {
      loadedEntries=state.entries; loadedRevision=nativeRevision; metrics.entryPasses++;
      loadedMessages=messagesFromEntries(state.entries);
    }
    return loadedMessages;
  }
  function abortable(task,signal) {
    return new Promise((resolve,reject) => {
      const abort=() => reject(new DOMException('Cancelled','AbortError'));
      if(signal.aborted) { Promise.resolve(task).catch(()=>{}); return abort(); }
      signal.addEventListener('abort',abort,{once:true});
      Promise.resolve(task).then(resolve,reject).finally(()=>signal.removeEventListener('abort',abort));
    });
  }
  function setMessages(all) { messages = all; messageIndex = new Map(all.map(m => [m.id,m])); }
  function domIndex() {
    if (!domDirty) return nodeIndex;
    domDirty = false; metrics.domPasses++; nodeIndex = new Map();
    for (const node of document.querySelectorAll('[data-chatgpt-search-message-ids], [data-message-id]')) {
      const r = node.getBoundingClientRect();
      if (!r.width || !r.height || node.checkVisibility?.({checkVisibilityCSS:true}) === false) continue;
      const ids = (node.getAttribute('data-chatgpt-search-message-ids') || '').split(/\s+/);
      const own = node.getAttribute('data-message-id'); if (own) ids.push(own);
      for (const id of ids) if (id && !nodeIndex.has(id)) nodeIndex.set(id,node);
    }
    return nodeIndex;
  }
  function findEntry(state, id) {
    if (entryArray !== state.entries || entryRevision !== nativeRevision) {
      entryArray = state.entries; entryRevision = nativeRevision; entryIndex = new Map();
      for (const e of state.entries) {
        for (const id of e.turn?.messageIds || []) entryIndex.set(id,e);
        for (const item of e.turn?.items || []) { if(item.messageId) entryIndex.set(item.messageId,e); if(item.id) entryIndex.set(item.id,e); }
      }
    }
    return entryIndex.get(id);
  }
  function remember(state) {
    if (!complete) return;
    const previous=cache.get(current);
    if(previous?.messages===messages && previous.fetched===lastSuccess) { previous.entries=state.entries; return; }
    cache.delete(current); cache.set(current,{ messages, entries:state.entries, fetched:lastSuccess });
    while (cache.size > 4 || [...cache.values()].reduce((n,c) => n+c.messages.length,0) > 5000) cache.delete(cache.keys().next().value);
  }
  function waitFor(check, timeout, signal) {
    return new Promise((resolve,reject) => {
      let done = false, observer, interval, deadline;
      const end = (value,e) => {
        if(done) return; done=true; observer?.disconnect(); clearInterval(interval); clearTimeout(deadline);
        signal?.removeEventListener('abort',abort); document.removeEventListener('scroll',probe,true); e ? reject(e) : resolve(value);
      };
      const abort = () => end(null,new DOMException('Cancelled','AbortError'));
      const probe = () => { try { const value=check(); if(value) end(value); } catch(e) {end(null,e);} };
      if(signal?.aborted) return abort();
      signal?.addEventListener('abort',abort,{once:true});
      observer = new MutationObserver(probe); observer.observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['class','style','hidden','data-message-id','data-chatgpt-search-message-ids']});
      document.addEventListener('scroll',probe,true);
      interval=setInterval(probe,50); deadline=setTimeout(() => end(null),timeout); probe();
    });
  }
  function loadNative(state) {
    const key=generation+':'+state.id;
    if(loading.has(key)) return loading.get(key);
    const controller=new AbortController(); controllers.add(controller); metrics.nativeLoads++;
    const timeout=setTimeout(() => controller.abort(),20000);
    const promise=abortable(Promise.resolve().then(() => state.ensureHistory(controller.signal)),controller.signal).finally(() => {
      clearTimeout(timeout); controllers.delete(controller); if(loading.get(key)===promise) loading.delete(key);updateHistoryIndicator();
    });
    loading.set(key,promise); updateHistoryIndicator(); return promise;
  }

  function closePreview() { hovered = -1; clearTimeout(delay); card.dataset.open = 'false'; }
  function showPreview(index, button, keyboard = false) {
    clearTimeout(delay); clearTimeout(dismiss);
    const show = () => {
      hovered = index;
      updateBookmark();
      number.textContent = String(index + 1).padStart(2, '0') + ' / ' + messages.length;
      scope.textContent = formatMessageTime(messages[index]?.sentAt);
      text.textContent = messages[index]?.text || '暂无可读消息';
      notice.textContent = error || '';
      updateHistoryIndicator();
      const b = button.getBoundingClientRect(), h = host.getBoundingClientRect();
      // Position instantly; only the small entrance transform animates.
      card.style.top = (Math.max(12, Math.min(b.top - 34, innerHeight - 280)) - h.top) + 'px';
      card.dataset.keyboard = String(keyboard); card.dataset.open = 'true';
    };
    if (keyboard || card.dataset.open === 'true') show(); else delay = setTimeout(show, 100);
  }
  function messageNode(id) {
    return domIndex().get(id);
  }
  function visible(node) {
    if (!node) return false;
    const r = node.getBoundingClientRect(); return r.height > 0 && r.top < innerHeight - 100 && r.bottom > 80;
  }
  async function jump(message) {
    const original = current;
    const ticket = ++jumpSequence, started = performance.now();
    jumpAbort?.abort(); const intent=new AbortController(); jumpAbort=intent;
    const valid=() => !stopped && current===original && ticket===jumpSequence;
    try {
      let state = nativeState();
      if (!state || state.id !== original) throw new Error('对话已切换，请稍候再试');
      const rendered=messageNode(message.id);
      let entry = findEntry(state,message.id);
      if (!rendered && !entry && state.ensureHistory) {
        notice.textContent = '正在加载历史位置…';
        const ready=new AbortController();
        const cancelReady=()=>ready.abort();intent.signal.addEventListener('abort',cancelReady,{once:true});
        try {
          await abortable(Promise.race([loadNative(state),waitFor(()=>{
            if(!valid())return true;
            const latest=nativeState();return latest?.id===original && findEntry(latest,message.id);
          },20000,ready.signal)]),intent.signal);
        } finally {ready.abort();intent.signal.removeEventListener('abort',cancelReady);}
        if(!valid()) return;
        await waitFor(() => {
          if(!valid()) return true;
          state=nativeState(); if(!state || state.id!==original) return false;
          entry=findEntry(state,message.id); return entry;
        },6000,intent.signal);
      }
      if(!valid()) return;
      state=nativeState(); if(!state || state.id!==original) return;
      entry=findEntry(state,message.id);
      if (rendered?.isConnected) {
        rendered.scrollIntoView({block:'start',behavior:'instant'});
      } else if (entry && state.api) {
        await state.api.scrollToKey(entry.turnKey || entry.id, row => row.querySelector('[data-chatgpt-search-message-ids~="' + CSS.escape(message.id) + '"]') || row, { align: 'top' });
      } else if (state.open) await state.open(message.id);
      else {
        const node = messageNode(message.id);
        if (!node) throw new Error('此版本无法加载这条历史消息的位置');
        node.scrollIntoView({ block: 'start', behavior: matchMedia('(prefers-reduced-motion:reduce)').matches ? 'instant' : 'smooth' });
      }
      await waitFor(() => visible(messageNode(message.id)),3000,intent.signal);
      if (!valid()) return;
      const node = messageNode(message.id);
      if (!visible(node)) throw new Error('目标尚未进入可见区域；目录已保留，可再次点击');
      error = ''; notice.textContent = '已定位';
      metrics.lastJumpMs = Math.round(performance.now()-started);
      if (!matchMedia('(prefers-reduced-motion:reduce)').matches)
        node.animate([{ opacity: .82 }, { opacity: 1 }], { duration: 160, easing: 'cubic-bezier(.23,1,.32,1)' });
    } catch (e) { if (valid() && !intent.signal.aborted) { error = e.name==='AbortError'?'加载历史位置超时，请重试':e.message; notice.textContent = error; } }
  }
  function render() {
    closePreview(); rail.replaceChildren(); buttons.clear(); activeButton=null;
    if (!messages.length) {
      const empty = document.createElement('span'); empty.id = 'empty'; empty.textContent = '⋯';
      empty.title = error || '正在读取用户消息'; rail.append(empty); return;
    }
    messages.forEach((m, i) => {
      const b = document.createElement('button'); b.className = 'item'; b.type = 'button';
      b.setAttribute('aria-label', `第 ${i + 1} 条用户消息：${m.text.slice(0,160)}`);
      b.setAttribute('aria-describedby', 'preview'); b.dataset.id = m.id;
      b.dataset.bookmarked=String(isBookmarked(m.id));
      if(isBookmarked(m.id)) b.setAttribute('aria-label',b.getAttribute('aria-label')+'，已收藏此轮对话');
      const bar = document.createElement('span'); bar.className = 'bar'; b.append(bar);
      b.addEventListener('pointerenter', e => { if (e.pointerType !== 'touch') showPreview(i, b); });
      b.addEventListener('pointerleave', () => { dismiss = setTimeout(closePreview, 90); });
      b.addEventListener('focus', () => showPreview(i, b, true));
      b.addEventListener('blur', e => {if(!card.contains(e.relatedTarget))closePreview();});
      b.addEventListener('click', () => jump(m));
      b.addEventListener('keydown', e => {
        if (e.key === 'Escape') closePreview();
        if (e.key === 'ArrowLeft') {e.preventDefault();showPreview(i,b,true);bookmark.focus();}
        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
          e.preventDefault(); rail.children[Math.max(0,Math.min(messages.length - 1,i + (e.key === 'ArrowDown' ? 1 : -1)))].focus();
        }
      }); rail.append(b); buttons.set(m.id,b);
    });
  }
  async function fullHistory(id, ticket) {
    const key=ticket+':'+id;
    if(fetching===key) return;
    fetching=key; metrics.historyRequests++; const started=performance.now();
    const controller = new AbortController(); controllers.add(controller);
    const timeout = setTimeout(() => controller.abort(), 20000);
    try {
      // Reuse the application's existing authenticated client. Never obtain,
      // copy, print, persist or forward its cookies or authentication tokens.
      const client = await abortable(historyClient(),controller.signal);
      if(controller.signal.aborted || stopped || ticket !== generation || current !== id) return;
      const data = await abortable(client.safeGet('/conversation/{conversation_id}', {
        parameters: { path: { conversation_id: id } }, signal: controller.signal
      }),controller.signal);
      if (stopped || ticket !== generation || current !== id) return;
      const all = activeBranch(data);
      const unchanged=messages.length===all.length && all.every((m,i)=>m.id===messages[i].id && m.text===messages[i].text);
      if(!unchanged || all.some((m,i)=>m.sentAt!==messages[i].sentAt)) setMessages(all);
      complete = true; error = ''; failures=0; lastSuccess=Date.now(); metrics.lastHistoryMs=Math.round(performance.now()-started);
      updateHistoryIndicator();
      if(!unchanged) render();
      else if(hovered>=0) { scope.textContent=formatMessageTime(messages[hovered]?.sentAt); notice.textContent=''; }
      const state=nativeState(); if(state?.id===id) remember(state);
    } catch (e) {
      if (ticket === generation && current === id && !stopped) {
        error = '完整历史读取失败：' + (e.name === 'AbortError' ? '连接超时' : e.message);
        complete = false; render();
        updateHistoryIndicator();
        failures++;
      }
    } finally { clearTimeout(timeout); controllers.delete(controller); if(fetching===key) fetching=''; }
  }
  async function tick() {
    if (stopped || busy) return;
    busy = true;
    try {
      const state = nativeState();
      if (!state) {
        host.style.display = 'none'; closePreview();
        if (current) { for (const c of controllers) c.abort(); generation++; }
        jumpSequence++; jumpAbort?.abort(); current = ''; setMessages([]); complete = false; rail.replaceChildren(); buttons.clear(); activeButton=null; return;
      }
      host.style.display = '';
      const anchor = state.anchor;
      const bounds = anchor?.closest('.thread-scroll-container, [data-request-input-activity-root]')?.getBoundingClientRect();
      host.style.right = Math.max(16, bounds?.width ? innerWidth - Math.min(innerWidth,bounds.right) + 16 : 18) + 'px';
      host.style.top = bounds?.height ? bounds.top + bounds.height / 2 + 'px' : '50%';
      const theme = document.documentElement.dataset.theme;
      host.dataset.theme = theme === 'light' ? 'light' : 'dark';
      if (state.id !== current) {
        jumpSequence++; jumpAbort?.abort();
        for (const c of controllers) c.abort();
        current = state.id; generation++; complete = false; error = ''; lastFetch = 0; lastSuccess=0; failures=0;
        domDirty=true;
        const cached=cache.get(current);
        const initial=entryMessages(state);
        const cachedIndex=cached ? new Map(cached.messages.map(m=>[m.id,m.text])) : null;
        if(cached && Date.now()-cached.fetched<60000 && initial.at(-1)?.id===cached.messages.at(-1)?.id && initial.every(m=>cachedIndex.get(m.id)===m.text)) {
          setMessages(cached.messages); complete=true; lastFetch=cached.fetched; lastSuccess=cached.fetched; metrics.cacheHits++;
        } else setMessages(initial);
        render();
      }
      const loaded = entryMessages(state);
      const needsUpdate = loaded.some(m => messageIndex.get(m.id)?.text !== m.text) || (loaded.length && complete && loaded.at(-1).id!==messages.at(-1)?.id);
      if(needsUpdate) { cache.delete(current); complete=false; setMessages(loaded); render(); }
      if ((!complete || needsUpdate || Date.now() - lastFetch > 60000) && Date.now() - lastFetch > Math.min(60000,8000 * 2 ** failures)) {
        lastFetch = Date.now(); fullHistory(current, generation);
      }
      const center = innerHeight / 2;
      let closest = null, distance = Infinity;
      for (const [id,node] of domIndex()) {
        if (!messageIndex.has(id)) continue;
        const r = node.getBoundingClientRect(), d = Math.abs(r.top - center);
        if (r.bottom > 80 && r.top < innerHeight - 100 && d < distance) { closest = id; distance = d; }
      }
      const nextButton=buttons.get(closest) || null;
      if(activeButton!==nextButton) {
        if(activeButton){activeButton.dataset.active='false';metrics.activeWrites++;}
        if(nextButton){nextButton.dataset.active='true';metrics.activeWrites++;}
        activeButton=nextButton;
      }
      if(complete && !needsUpdate) remember(state);
    } catch (e) { error = '当前版本适配失败：' + e.message; }
    finally { busy = false; }
  }
  function schedule() { if(stopped || frame) return; frame=requestAnimationFrame(() => {frame=0;tick();}); }
  card.addEventListener('pointerenter',()=>clearTimeout(dismiss));
  card.addEventListener('pointerleave',()=>{if(!card.contains(shadow.activeElement))dismiss=setTimeout(closePreview,90);});
  card.addEventListener('keydown',e=>{if(e.key==='Escape'){e.preventDefault();const b=rail.children[hovered];b?.focus();closePreview();}});
  card.addEventListener('focusout',()=>setTimeout(()=>{if(!card.contains(shadow.activeElement) && !rail.contains(shadow.activeElement))closePreview();},0));
  bookmark.addEventListener('click',e=>{
    e.stopPropagation();const m=messages[hovered];if(!m || !current)return;
    const previous=bookmarks[current] || new Set(), next=new Set(previous);
    if(next.has(m.id))next.delete(m.id);else next.add(m.id);
    const saved=Object.create(null);for(const [id,ids] of Object.entries(bookmarks))if(ids.size)saved[id]=[...ids];
    if(next.size)saved[current]=[...next];else delete saved[current];
    try {if(Object.values(saved).reduce((n,ids)=>n+ids.length,0)>5000)throw Error('书签数量已达上限');
      localStorage.setItem(bookmarkKey,JSON.stringify({v:1,conversations:saved}));bookmarks[current]=next;
      const b=rail.children[hovered];if(b){b.dataset.bookmarked=String(next.has(m.id));b.setAttribute('aria-label',`第 ${hovered+1} 条用户消息：${m.text.slice(0,160)}`+(next.has(m.id)?'，已收藏此轮对话':''));}
      updateBookmark();notice.textContent=next.has(m.id)?'已收藏':'已移除收藏';
    }catch(e){notice.textContent='书签保存失败：'+e.message;}
  });
  const changes=new MutationObserver(records => {
    if(records.every(r => r.target===host || host.contains(r.target))) return;
    const selector='[data-chatgpt-search-message-ids], [data-chatgpt-search-unit-key], [data-message-id]';
    const containsMessage=node=>node.nodeType===1 && (node.matches(selector)||node.querySelector(selector));
    const structural=records.some(r=>r.type==='attributes' && r.attributeName.startsWith('data-') || r.type==='childList' && (containsMessage(r.target) && r.target.matches(selector) || [...r.addedNodes,...r.removedNodes].some(containsMessage)));
    if(structural){domDirty=true;nativeRevision++;}
    else if(records.some(r=>r.type==='attributes'))domDirty=true;
    schedule();
  });
  changes.observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['class','style','hidden','data-message-id','data-chatgpt-search-message-ids']});
  const onScroll=() => schedule();
  document.addEventListener('scroll',onScroll,true); window.addEventListener('resize',onScroll);
  const api = { version: VERSION, build: BUILD,
    get status() { return { count: messages.length, complete, error, connected: !!current, theme:document.documentElement.dataset.theme || host.dataset.theme, conversationId:current, bookmarks:bookmarks[current]?.size || 0, performance:{...metrics} }; },
    stop() { stopped = true; clearInterval(timer); clearTimeout(delay); clearTimeout(dismiss); cancelAnimationFrame(frame); changes.disconnect(); document.removeEventListener('scroll',onScroll,true); window.removeEventListener('resize',onScroll); jumpSequence++; jumpAbort?.abort(); for (const c of controllers) c.abort(); cache.clear(); loading.clear(); buttons.clear(); nodeIndex.clear(); messageIndex.clear(); entryIndex?.clear(); loadedEntries=null; loadedMessages=[]; setMessages([]); host.remove(); delete window.__chatRail; },
    refresh() { cache.delete(current); lastFetch = 0; complete = false; tick(); }
  };
  window.__chatRail = api;
  if (window.__CHAT_RAIL_TEST__) api.test = { activeBranch, nativeState, jump, tick };
  tick(); timer = setInterval(tick, 500);
  return { installed: true, version: VERSION };
})();
