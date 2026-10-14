'use strict';
document.addEventListener('click', (event) => {
    if (event.target instanceof Element && event.target.closest('[data-bank-print]')) window.print();
});
