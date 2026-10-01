;;; XrefFolder.lsp
;;; Attach every DWG in a chosen folder as an xref at 0,0,0 (scale 1, rotation 0).
;;;
;;; Commands:
;;;   XREFFOLDER  - pick a folder, choose Overlay/Attach, attach all DWGs in it
;;;
;;; Behaviour:
;;;   - Skips the current drawing
;;;   - Skips xrefs/blocks already in the drawing with the same name
;;;   - Skips any file with "_" in the name (the separator/heading files,
;;;     e.g. 251681-...-arch_______architectural_____.dwg)
;;;   - Puts all xrefs on layer XREF (created if missing)
;;;   - Uses relative paths when the current drawing has been saved
;;;
;;; Load with APPLOAD (or drag the file into the drawing), then type XREFFOLDER.

(vl-load-com)

;; Folder picker via Windows Shell; falls back to "pick any DWG in the folder".
(defun xf:browse-folder (msg / sh fld self path)
  (setq sh (vlax-get-or-create-object "Shell.Application"))
  (if sh
    (progn
      (setq fld (vl-catch-all-apply 'vlax-invoke-method
                  (list sh 'BrowseForFolder 0 msg 0 "")))
      (if (and fld (not (vl-catch-all-error-p fld)))
        (progn
          (setq self (vlax-get-property fld 'Self))
          (setq path (vlax-get-property self 'Path))
          (vlax-release-object self)
          (vlax-release-object fld)))
      (vlax-release-object sh)))
  (if (not (and path (vl-file-directory-p path)))
    (if (setq path (getfiled "Pick any DWG in the xref folder" (getvar "DWGPREFIX") "dwg" 0))
      (setq path (vl-filename-directory path))))
  (if path (vl-string-right-trim "\\/" path)))

;; Relative path from the host drawing's folder to TARGET (same drive only).
(defun xf:relative-path (target / base bparts tparts ups)
  (setq base (vl-string-right-trim "\\" (getvar "DWGPREFIX")))
  (if (and (= (getvar "DWGTITLED") 1)
           (= (strcase (substr base 1 2)) (strcase (substr target 1 2))))
    (progn
      (setq bparts (xf:split base "\\")
            tparts (xf:split target "\\"))
      (while (and bparts tparts (= (strcase (car bparts)) (strcase (car tparts))))
        (setq bparts (cdr bparts) tparts (cdr tparts)))
      (setq ups "")
      (if bparts
        (foreach p bparts (setq ups (strcat ups "..\\")))
        (setq ups ".\\"))
      (strcat ups (xf:join tparts "\\")))))

(defun xf:split (str del / pos lst)
  (while (setq pos (vl-string-search del str))
    (setq lst (cons (substr str 1 pos) lst)
          str (substr str (+ pos 1 (strlen del)))))
  (reverse (cons str lst)))

(defun xf:join (lst del / out)
  (setq out (car lst))
  (foreach s (cdr lst) (setq out (strcat out del s)))
  out)

(defun xf:ensure-layer (doc name / layers lay)
  (setq layers (vla-get-Layers doc))
  (if (vl-catch-all-error-p (setq lay (vl-catch-all-apply 'vla-Item (list layers name))))
    (setq lay (vla-Add layers name)))
  lay)

(defun c:XREFFOLDER (/ doc ms folder files mode overlay host
                       full name rel res blk added skipped failed)
  (setq doc  (vla-get-ActiveDocument (vlax-get-acad-object))
        ms   (vla-get-ModelSpace doc)
        host (strcase (strcat (getvar "DWGPREFIX") (getvar "DWGNAME"))))

  (if (not (setq folder (xf:browse-folder "Select folder containing xref DWGs")))
    (progn (princ "\nNo folder selected.") (exit)))

  (setq files (vl-sort (vl-directory-files folder "*.dwg" 1) '<))
  (if (not files)
    (progn (princ (strcat "\nNo DWG files found in " folder)) (exit)))
  (princ (strcat "\nFound " (itoa (length files)) " DWG(s) in " folder))

  (initget "Overlay Attach")
  (setq mode (getkword "\nXref type [Overlay/Attach] <Overlay>: "))
  (setq overlay (if (= mode "Attach") :vlax-false :vlax-true))

  (xf:ensure-layer doc "XREF")
  (vla-StartUndoMark doc)
  (setq added 0 skipped 0 failed 0)

  (foreach f files
    (setq full (strcat folder "\\" f)
          name (vl-filename-base f))
    (cond
      ((= (strcase full) host)
       (setq skipped (1+ skipped)))
      ((vl-string-search "_" f)
       (setq skipped (1+ skipped)))
      ((tblsearch "BLOCK" name)
       (princ (strcat "\n  Already in drawing, skipped: " name))
       (setq skipped (1+ skipped)))
      (t
       (princ (strcat "\n  Attaching: " f))
       (setq res (vl-catch-all-apply 'vla-AttachExternalReference
                   (list ms full name (vlax-3d-point '(0. 0. 0.)) 1. 1. 1. 0. overlay)))
       (if (vl-catch-all-error-p res)
         (progn
           (princ (strcat "  -> FAILED: " (vl-catch-all-error-message res)))
           (setq failed (1+ failed)))
         (progn
           (vla-put-Layer res "XREF")
           ;; switch saved path to relative where possible
           (if (and (setq rel (xf:relative-path full))
                    (setq blk (vla-Item (vla-get-Blocks doc) name)))
             (vl-catch-all-apply 'vla-put-Path (list blk rel)))
           (setq added (1+ added)))))))

  (vla-EndUndoMark doc)
  (vla-ZoomExtents (vlax-get-acad-object))
  (princ (strcat "\n\nXREFFOLDER done: " (itoa added) " attached, "
                 (itoa skipped) " skipped, " (itoa failed) " failed."))
  (princ))

(princ "\nXrefFolder loaded. Type XREFFOLDER to attach all DWGs in a folder.")
(princ)
