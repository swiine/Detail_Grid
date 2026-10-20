;;; DetailGrid.lsp
;;; Attaches every DWG in a details folder as an xref and lays them out
;;; in a grid, left to right then top to bottom.
;;;
;;; Usage:
;;;   1. APPLOAD this file (or add it to the Startup Suite).
;;;   2. Run DETAILGRID.
;;;   3. Pick any DWG inside the folder to attach (e.g. ...\Details\Notes),
;;;      then the top-left insertion point, number of columns and gap.
;;;
;;; Files whose names start with "__" (e.g. __becl.dwg) are skipped, as are
;;; files already attached/defined as blocks in the current drawing.

(vl-load-com)

;;; ---- Settings -------------------------------------------------------------

;; Folder to attach from. Leave nil to be asked each time.
;; Example: "C:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Details\\Notes"
(setq *DG:Folder* nil)

;; Wildcard for files to attach.
(setq *DG:Pattern* "*.dwg")

;; Prefix of files to skip.
(setq *DG:SkipPrefix* "__")

;; T = attach as overlay, nil = attachment.
(setq *DG:Overlay* nil)

;; Defaults offered at the prompts.
(setq *DG:Columns* 4)
(setq *DG:Gap* 50.0)

;;; ---- Helpers --------------------------------------------------------------

(defun DG:AddSlash (dir)
  (if (wcmatch dir "*\\,*/") dir (strcat dir "\\"))
)

(defun DG:GetFolder (/ file)
  (cond
    ((and *DG:Folder* (vl-file-directory-p *DG:Folder*)) *DG:Folder*)
    ((setq file (getfiled "Select any drawing in the folder to attach" "" "dwg" 0))
     (vl-filename-directory file))
  )
)

(defun DG:ListFiles (dir / files)
  (setq files (vl-directory-files dir *DG:Pattern* 1))
  (setq files
    (vl-remove-if
      '(lambda (f) (wcmatch (strcase f) (strcat (strcase *DG:SkipPrefix*) "*")))
      files
    )
  )
  (acad_strlsort files)
)

(defun DG:CurrentSpace (doc)
  (if (and (= (getvar "TILEMODE") 0) (= (getvar "CVPORT") 1))
    (vla-get-PaperSpace doc)
    (vla-get-ModelSpace doc)
  )
)

;;; ---- Command --------------------------------------------------------------

(defun c:DETAILGRID (/ *error* doc space dir files pt cols gap
                       col cx cy rowH name ref minp maxp mn mx w h
                       count skipped)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*"))
      (princ (strcat "\nDETAILGRID error: " msg))
    )
    (princ)
  )

  (cond
    ((not (setq dir (DG:GetFolder)))
     (princ "\nNo folder selected."))
    ((not (setq files (DG:ListFiles dir)))
     (princ (strcat "\nNo drawings found in " dir)))
    (T
     (setq dir (DG:AddSlash dir))
     (princ (strcat "\n" (itoa (length files)) " drawing(s) found in " dir))

     (setq pt (getpoint "\nTop-left corner of grid <0,0>: "))
     (if (not pt) (setq pt '(0.0 0.0 0.0)))

     (initget 6)
     (setq cols (getint (strcat "\nNumber of columns <" (itoa *DG:Columns*) ">: ")))
     (if cols (setq *DG:Columns* cols) (setq cols *DG:Columns*))

     (initget 4)
     (setq gap (getdist (strcat "\nGap between details <" (rtos *DG:Gap* 2 2) ">: ")))
     (if gap (setq *DG:Gap* gap) (setq gap *DG:Gap*))

     (vla-StartUndoMark doc)
     (setq space   (DG:CurrentSpace doc)
           col     0
           cx      (car pt)
           cy      (cadr pt)
           rowH    0.0
           count   0
           skipped 0)

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
                     (if *DG:Overlay* :vlax-true :vlax-false))))
           (if (vl-catch-all-error-p ref)
             (progn
               (princ (strcat "\n  Failed " file ": " (vl-catch-all-error-message ref)))
               (setq skipped (1+ skipped)))
             (progn
               ;; Size the xref, then move its top-left corner to the grid cell.
               (vla-GetBoundingBox ref 'minp 'maxp)
               (setq mn (vlax-safearray->list minp)
                     mx (vlax-safearray->list maxp)
                     w  (- (car mx) (car mn))
                     h  (- (cadr mx) (cadr mn)))
               (vla-Move ref
                 (vlax-3d-point (list (car mn) (cadr mx) 0.0))
                 (vlax-3d-point (list cx cy 0.0)))
               (setq rowH  (max rowH h)
                     cx    (+ cx w gap)
                     col   (1+ col)
                     count (1+ count))
               (princ (strcat "\n  Attached " file))
               ;; Wrap to the next row.
               (if (>= col cols)
                 (setq col  0
                       cx   (car pt)
                       cy   (- cy rowH gap)
                       rowH 0.0))
             )
           )
         )
       )
     )
     (vla-EndUndoMark doc)
     (vla-Regen doc acActiveViewport)
     (princ (strcat "\nDETAILGRID: attached " (itoa count)
                    ", skipped " (itoa skipped) "."))
    )
  )
  (princ)
)

(princ "\nDetailGrid loaded. Type DETAILGRID to run.")
(princ)
