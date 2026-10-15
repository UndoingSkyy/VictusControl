(() => {
  'use strict';
  let effect = null;
  let failed = false;
  let renderedLight = false;
  let state = { light: false, active: false };
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  function stop() {
    if (effect) {
      // Vanta 0.5.24 removes the line mesh before its scene cleanup, and does
      // not dispose the WebGL renderer. Release both when parking in the tray.
      const renderer = effect.renderer;
      const lines = effect.linesMesh;
      effect.destroy();
      lines?.geometry?.dispose();
      lines?.material?.dispose();
      renderer?.dispose();
      renderer?.forceContextLoss();
    }
    effect = null;
  }

  function sync() {
    document.documentElement.classList.toggle('light', state.light);
    if (!state.active || document.hidden || reducedMotion.matches) {
      stop();
      return;
    }
    const colors = {
      color: state.light ? 0x737772 : 0x777b80,
      backgroundColor: state.light ? 0xeae9e3 : 0x1a1b1d
    };
    // NET chooses additive/subtractive blending only on initialization.
    if (effect && renderedLight !== state.light) stop();
    if (effect) {
      effect.setOptions(colors);
      return;
    }
    if (failed) return;
    try {
      effect = VANTA.NET({
        el: '#network',
        mouseControls: true,
        touchControls: true,
        gyroControls: false,
        minHeight: 200.00,
        minWidth: 200.00,
        scale: 1.00,
        scaleMobile: 1.00,
        points: 8,
        maxDistance: 22,
        spacing: 20,
        ...colors
      });
      if (!effect.renderer || !effect.scene) throw new Error('WebGL unavailable');
      renderedLight = state.light;
    } catch (error) {
      stop();
      failed = true;
      // A failed WebGL renderer must never prevent access to hardware controls.
      document.querySelectorAll('canvas').forEach(canvas => canvas.remove());
      window.chrome?.webview?.postMessage({ type: 'unavailable' });
    }
  }

  window.chrome?.webview?.addEventListener('message', event => {
    const message = event.data;
    if (message.type === 'state') {
      state = { light: !!message.light, active: !!message.active };
      sync();
    } else if (message.type === 'pointer' && effect) {
      // WPF owns input. Relay coordinates without intercepting buttons or sliders.
      window.dispatchEvent(new MouseEvent('mousemove', {
        clientX: Math.max(0, Math.min(1, message.x)) * innerWidth,
        clientY: Math.max(0, Math.min(1, message.y)) * innerHeight
      }));
    }
  });
  document.addEventListener('visibilitychange', sync);
  reducedMotion.addEventListener('change', sync);
  window.addEventListener('pagehide', stop);
  window.chrome?.webview?.postMessage({ type: 'ready' });
})();
