(function () {
  var c = document.createElement('canvas');
  var gl = c.getContext('webgl');
  var dbg = gl && gl.getExtension('WEBGL_debug_renderer_info');
  var uad = navigator.userAgentData;
  return {
    ua: navigator.userAgent,
    plat: navigator.platform,
    vendor: navigator.vendor,
    cores: navigator.hardwareConcurrency,
    devMem: navigator.deviceMemory,
    touch: navigator.maxTouchPoints,
    screen: screen.width + 'x' + screen.height,
    avail: screen.availWidth + 'x' + screen.availHeight,
    inner: window.innerWidth + 'x' + window.innerHeight,
    outer: window.outerWidth + 'x' + window.outerHeight,
    dpr: window.devicePixelRatio,
    visual: visualViewport ? Math.round(visualViewport.width) + 'x' + Math.round(visualViewport.height) : null,
    glVendor: gl ? gl.getParameter(gl.VENDOR) : null,
    glRenderer: gl ? gl.getParameter(gl.RENDERER) : null,
    unmasked: dbg ? (gl.getParameter(dbg.UNMASKED_VENDOR_WEBGL) + ' || ' + gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL)) : 'extension null',
    chrome: typeof window.chrome,
    uad: uad ? JSON.stringify(uad.brands) + ' plat=' + uad.platform + ' model=' + uad.model + ' pv=' + uad.platformVersion : 'none',
    tz: Intl.DateTimeFormat().resolvedOptions().timeZone,
    lang: navigator.language,
    langs: JSON.stringify(navigator.languages),
    plugins: navigator.plugins.length,
    media500: matchMedia('(max-width: 500px)').matches
  };
})()
