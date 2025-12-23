document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("form.preventdoubleclick").forEach(form => {
        form.addEventListener("submit", (e) => {
            let submitElement = form.querySelector("button[type='submit'], input[type='submit']");
            if (submitElement && !submitElement.disabled) {
                submitElement.disabled = true;

                if (submitElement.tagName === "BUTTON") {
                    submitElement.dataset.originalText = submitElement.innerText;
                    submitElement.innerText = "Processing...";
                } else if (submitElement.tagName === "INPUT") {
                    submitElement.dataset.originalText = submitElement.value;
                    submitElement.value = "Processing...";
                }

                setTimeout(() => form.submit(), 10);

                e.preventDefault();
            }
        });
    });
});
