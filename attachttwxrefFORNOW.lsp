;;; attachttwxrefFORNOW.lsp
;;; Attaches every TTW_*.dwg in a folder as an xref at 0,0.
;;;
;;; Usage:
;;;   1. APPLOAD this file (or add it to the Startup Suite).
;;;   2. Run ATTACHTTWXREFFORNOW.
;;;   3. Pick any DWG inside the folder to attach (e.g. ...\Details\Notes).
;;;
;;; Files already attached/defined as blocks in the current drawing are skipped.

(vl-load-com)

;;; ---- Settings -------------------------------------------------------------

;; Folder to attach from. Leave nil to be asked each time.
;; Example: "C:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Details\\Notes"
(setq *ATX:Folder* nil)

;; Wildcard for files to attach.
(setq *ATX:Pattern* "TTW_*.dwg")

;; T = attach as overlay, nil = attachment.
(setq *ATX:Overlay* nil)

;;; ---- Helpers --------------------------------------------------------------

(defun ATX:AddSlash (dir)
  (if (wcmatch dir "*\\,*/") dir (strcat dir "\\"))
)

(defun ATX:GetFolder (/ file)
  (cond
    ((and *ATX:Folder* (vl-file-directory-p *ATX:Folder*)) *ATX:Folder*)
    ((setq file (getfiled "Select any drawing in the folder to attach" "" "dwg" 0))
     (vl-filename-directory file))
  )
)

(defun ATX:CurrentSpace (doc)
  (if (and (= (getvar "TILEMODE") 0) (= (getvar "CVPORT") 1))
    (vla-get-PaperSpace doc)
    (vla-get-ModelSpace doc)
  )
)

;;; ---- Command --------------------------------------------------------------

(defun c:ATTACHTTWXREFFORNOW (/ *error* doc space dir files name ref count skipped)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*"))
      (princ (strcat "\nATTACHTTWXREFFORNOW error: " msg))
    )
    (princ)
  )

  (cond
    ((not (setq dir (ATX:GetFolder)))
     (princ "\nNo folder selected."))
    ((not (setq files (acad_strlsort (vl-directory-files dir *ATX:Pattern* 1))))
     (princ (strcat "\nNo " *ATX:Pattern* " drawings found in " dir)))
    (T
     (setq dir     (ATX:AddSlash dir)
           space   (ATX:CurrentSpace doc)
           count   0
           skipped 0)
     (vla-StartUndoMark doc)
     (foreach file files
       (setq name (vl-filename-base file))
       (if (tblsearch "BLOCK" name)
         (progn
           (princ (strcat "\n  Skipped " file " (already in drawing)"))
           (setq skipped (1+ skipped)))
         (progn
           (setq ref
             (vl-catch-all-apply 'vla-AttachExternalReference
               (list space (strcat dir file) name
                     (vlax-3d-point '(0.0 0.0 0.0))
                     1.0 1.0 1.0 0.0
                     (if *ATX:Overlay* :vlax-true :vlax-false))))
           (if (vl-catch-all-error-p ref)
             (progn
               (princ (strcat "\n  Failed " file ": " (vl-catch-all-error-message ref)))
               (setq skipped (1+ skipped)))
             (progn
               (princ (strcat "\n  Attached " file))
               (setq count (1+ count)))
           )
         )
       )
     )
     (vla-EndUndoMark doc)
     (vla-Regen doc acActiveViewport)
     (princ (strcat "\nATTACHTTWXREFFORNOW: attached " (itoa count)
                    ", skipped " (itoa skipped) "."))
    )
  )
  (princ)
)

(princ "\nattachttwxrefFORNOW loaded. Type ATTACHTTWXREFFORNOW to run.")
(princ)
