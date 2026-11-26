// scalar has no markdown download option, the json button is cloned
const { base, documents, fallback } = JSON.parse(document.querySelector('script[data-markdown]').dataset.markdown);

const currentDocument = () => {
    const slug = location.pathname.slice(base.length).split('/')[1];
    return Object.hasOwn(documents, slug) ? documents[slug] : fallback;
};

const download = () => {
    const { url, name } = currentDocument();
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
};

const inject = () => {
    const box = document.querySelector('.download-container');
    const json = box?.querySelector('button.download-button');
    if (!json || box.nextElementSibling?.hasAttribute('data-portway-md')) return;

    const wrap = box.cloneNode(false);
    wrap.classList.remove('download-both');
    wrap.setAttribute('data-portway-md', '');

    const button = json.cloneNode(true);
    button.setAttribute('aria-label', 'Download OpenAPI Document as Markdown');
    const badge = button.querySelector('.extension');
    if (badge) badge.textContent = 'md';
    button.addEventListener('click', download);

    wrap.append(button);
    box.after(wrap);
};

new MutationObserver(inject).observe(document.body, { childList: true, subtree: true });
