import hljs from '/js/highlight-11.11.1.min.js';

export function highlight(element, language) {
    if (!element || !language) {
        return;
    }

    const text = element.textContent ?? '';
    element.innerHTML = hljs.highlight(text, { language }).value;
    element.classList.add('hljs');
}
