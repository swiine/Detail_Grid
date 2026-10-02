;; TTW Linemarking (.NET) - DRAFT, not yet tested live.
;; Append to the acaddoc.lsp that already autoloads the LISP tools. Loads the plugin once per
;; Civil 3D session: acaddoc.lsp runs for every drawing, and each drawing has its own LISP
;; variables, so the "already loaded" flag lives on the session-wide blackboard (vl-bb-*).
;; Runs from S::STARTUP because NETLOAD shouldn't be called while acaddoc.lsp is still loading.

(defun ttw-netload-linemarking (/ dll)
  (setq dll "A:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Scripts\\TTWLinemarking.dll")
  (if (and (not (vl-bb-ref 'ttw-linemarking-loaded)) (findfile dll))
    (progn
      (command-s "_.NETLOAD" dll)
      (vl-bb-set 'ttw-linemarking-loaded T)
    )
  )
  (princ)
)

(defun-q ttw-linemarking-startup () (ttw-netload-linemarking))
(setq S::STARTUP (append S::STARTUP ttw-linemarking-startup))
